using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopyTools.Application;
using CopyTools.Domain;
using CopyTools.Engine;
using CopyTools.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;
return await CopyToolsCli.RunAsync(args);

internal static class CopyToolsCli
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string[] arguments)
    {
        try
        {
            var parsed = CliArguments.Parse(arguments);
            if (parsed.Command is null or "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            var dataDirectory = parsed.Get("data-dir") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyTools");
            var repository = new SqliteCopyToolsRepository(Path.Combine(Path.GetFullPath(dataDirectory), "copytools.db"));
            await repository.InitializeAsync();
            var workflow = new CopyWorkflow(new CopyPlanService(snapshotStore: repository));
            var runner = new ScheduledTaskRunner(repository, workflow,
                settingsStore: new JsonSettingsStore(Path.Combine(Path.GetFullPath(dataDirectory), "settings.json")));

            return parsed.Command switch
            {
                "tasks" => await ListTasksAsync(repository, parsed.Json),
                "preview" => await PreviewAsync(repository, workflow, parsed),
                "run" => await RunTaskAsync(repository, runner, parsed),
                "queue" => await RunQueueAsync(repository, runner, parsed),
                "incomplete" => await ListIncompleteAsync(repository, parsed.Json),
                "resume" => await ResumeAsync(repository, workflow, parsed),
                "copy" => await RunAdHocAsync(workflow, parsed),
                _ => UnknownCommand(parsed.Command),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("操作已取消。");
            return 130;
        }
        catch (Exception exception) when (exception is
                   ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> ListTasksAsync(ICopyToolsRepository repository, bool json)
    {
        var tasks = await repository.GetTasksAsync();
        if (json)
        {
            WriteJson(tasks.Select(task => new
            {
                task.Id, task.Name, task.Request.Mode, task.Request.SourceDirectory,
                task.Request.TargetDirectory, task.UpdatedAtUtc,
            }));
        }
        else
        {
            foreach (var task in tasks)
            {
                Console.WriteLine($"{task.Id:D}\t{task.Name}\t{task.Request.Mode}\t{task.Request.SourceDirectory} -> {task.Request.TargetDirectory}");
            }
        }
        return 0;
    }

    private static async Task<int> PreviewAsync(
        ICopyToolsRepository repository,
        CopyWorkflow workflow,
        CliArguments arguments)
    {
        var task = await RequireTaskAsync(repository, arguments.RequireGuid("task"));
        var request = NormalizeSynchronization(task);
        var plan = await workflow.CreatePlanAsync(request);
        var summary = new
        {
            task.Id, task.Name, request.Mode, plan.TotalFiles, plan.OperationsToExecute,
            plan.NewFiles, plan.FilesToOverwrite, plan.SkippedFiles,
            plan.FilesToQuarantine, plan.SourceFilesToRemove, plan.Conflicts, plan.BytesToCopy,
            Items = arguments.Has("details") ? plan.Items : null,
        };
        if (arguments.Json) WriteJson(summary);
        else Console.WriteLine(
            $"任务：{task.Name}\n模式：{request.Mode}\n执行项：{plan.OperationsToExecute:N0}\n"
            + $"新增：{plan.NewFiles:N0}，覆盖：{plan.FilesToOverwrite:N0}，隔离：{plan.FilesToQuarantine:N0}，冲突：{plan.Conflicts:N0}\n"
            + $"数据量：{plan.BytesToCopy:N0} 字节");
        return plan.OperationsToExecute == 0 ? 0 : 10;
    }

    private static async Task<int> RunTaskAsync(
        ICopyToolsRepository repository,
        ScheduledTaskRunner runner,
        CliArguments arguments)
    {
        var id = arguments.RequireGuid("task");
        var result = await runner.RunAsync(new TaskSchedule(
            id, true, ScheduleTrigger.ApplicationStartup, TimeSpan.Zero,
            AllowOverwrite: arguments.Has("allow-overwrite"),
            AllowQuarantine: arguments.Has("allow-quarantine")));
        WriteResult(result, arguments.Json);
        return ExitCode(result.Status);
    }

    private static async Task<int> RunQueueAsync(
        ICopyToolsRepository repository,
        ScheduledTaskRunner runner,
        CliArguments arguments)
    {
        var ids = arguments.Positionals.Select(ParseGuid).ToArray();
        if (ids.Length == 0) throw new ArgumentException("queue 命令必须提供至少一个任务 ID。");
        var parallel = arguments.GetInt("parallel", 1, 1, 16);
        var result = await new TaskQueueService(repository, runner).RunAsync(ids, parallel);
        if (arguments.Json) WriteJson(result);
        else foreach (var item in result.Items)
            Console.WriteLine($"{item.TaskId:D}\t{item.Status}\t{item.TaskName}\t{item.Message}");
        return result.IsSuccessful ? 0 : 20;
    }

    private static async Task<int> ListIncompleteAsync(ICopyToolsRepository repository, bool json)
    {
        var executions = await repository.GetIncompleteExecutionsAsync();
        if (json) WriteJson(executions);
        else foreach (var item in executions)
            Console.WriteLine($"{item.TaskId:D}\t{item.StartedAtUtc:O}\t已完成 {item.CompletedItems.Count:N0} 项");
        return executions.Count == 0 ? 0 : 10;
    }

    private static async Task<int> ResumeAsync(
        ICopyToolsRepository repository,
        CopyWorkflow workflow,
        CliArguments arguments)
    {
        var id = arguments.RequireGuid("task");
        var resumable = new ResumableExecutionService(repository, workflow);
        var (_, result) = await resumable.ResumeAsync(id, CreateConsoleProgress(arguments));
        var task = await RequireTaskAsync(repository, id);
        await repository.SaveRunAsync(RunHistoryFactory.FromExecution(
            task, await workflow.CreatePlanAsync(NormalizeSynchronization(task)), result));
        WriteExecution(task.Name, result, arguments.Json);
        return ExitCode(result.Status);
    }

    private static async Task<int> RunAdHocAsync(CopyWorkflow workflow, CliArguments arguments)
    {
        var source = arguments.Require("source");
        var target = arguments.Require("target");
        var mode = Enum.TryParse<CopyMode>(arguments.Get("mode") ?? nameof(CopyMode.AccumulateSync), true, out var parsedMode)
            ? parsedMode : throw new ArgumentException("无效的复制模式。");
        var verification = ResolveVerification(arguments);
        var request = new CopyTaskRequest(
            source, target, mode,
            VerificationMode: verification,
            ExistingFilePolicy: arguments.Has("allow-overwrite") ? ExistingFilePolicy.Overwrite : ExistingFilePolicy.Skip,
            Performance: new PerformanceOptions(1024 * 1024,
                MaxParallelFiles: arguments.GetInt("parallel", 1, 1, 16), AutoTune: false));
        var plan = await workflow.CreatePlanAsync(request);
        if ((plan.FilesToOverwrite > 0 || plan.FilesToQuarantine > 0 || plan.SourceFilesToRemove > 0)
            && !arguments.Has("confirm-dangerous"))
        {
            Console.Error.WriteLine("计划包含覆盖或隔离操作；请先使用 preview/桌面端检查，或显式传入 --confirm-dangerous。");
            return 12;
        }

        if (arguments.Has("dry-run"))
        {
            var summary = new
            {
                Mode = plan.Request.Mode, plan.TotalFiles, plan.OperationsToExecute, plan.NewFiles,
                plan.FilesToOverwrite, plan.SkippedFiles, plan.FilesToQuarantine,
                plan.SourceFilesToRemove, plan.Conflicts, plan.BytesToCopy,
            };
            if (arguments.Json) WriteJson(summary);
            else Console.WriteLine(
                $"预演：新增 {plan.NewFiles:N0}，覆盖 {plan.FilesToOverwrite:N0}，隔离 {plan.FilesToQuarantine:N0}，"
                + $"清理 {plan.SourceFilesToRemove:N0}，数据量 {plan.BytesToCopy:N0} 字节");
            return plan.OperationsToExecute == 0 ? 0 : 10;
        }

        var result = await workflow.ExecuteAsync(plan, CreateConsoleProgress(arguments));
        WriteExecution("临时命令行任务", result, arguments.Json);
        return ExitCode(result.Status);
    }

    private static VerificationMode ResolveVerification(CliArguments arguments) =>
        (arguments.Get("verify") ?? string.Empty).ToLowerInvariant() switch
        {
            "sha256" => VerificationMode.Sha256,
            "sampled" or "sampled-sha256" => VerificationMode.SampledSha256,
            "quick" => VerificationMode.Quick,
            _ => arguments.Has("sha256") ? VerificationMode.Sha256 : VerificationMode.Quick,
        };

    private static IProgress<CopyProgress>? CreateConsoleProgress(CliArguments arguments) =>
        arguments.Json || arguments.Has("quiet") ? null : new Progress<CopyProgress>(value =>
            Console.Error.WriteLine($"{value.CompletedFiles}/{value.TotalFiles}\t{value.CurrentRelativePath}\t{value.Status}"));

    private static async Task<SavedCopyTask> RequireTaskAsync(ICopyToolsRepository repository, Guid id) =>
        await repository.GetTaskAsync(id) ?? throw new ArgumentException($"任务不存在：{id:D}。");

    private static CopyTaskRequest NormalizeSynchronization(SavedCopyTask task)
    {
        var request = PathTemplateExpander.Expand(task.Request, task.Name);
        return request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
            ? request with { SynchronizationId = task.Id }
            : request;
    }

    private static void WriteResult(ScheduledRunResult result, bool json)
    {
        if (json) WriteJson(result);
        else Console.WriteLine($"{result.Status}\t{result.TaskName}\t{result.Message}");
    }

    private static void WriteExecution(string name, CopyExecutionResult result, bool json)
    {
        if (json) WriteJson(new { TaskName = name, result.Status, result.SuccessfulFiles, result.SkippedFiles, FailedFiles = result.Failures.Count, result.CopiedBytes, result.WasCancelled });
        else Console.WriteLine($"{result.Status}\t{name}\t成功 {result.SuccessfulFiles:N0}\t失败 {result.Failures.Count:N0}\t{result.CopiedBytes:N0} 字节");
    }

    private static void WriteJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    private static int ExitCode(string status) => status switch { "成功" => 0, "等待人工确认" => 12, "已取消" => 130, _ => 20 };
    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var id) ? id : throw new ArgumentException($"无效任务 ID：{value}。");
    private static int UnknownCommand(string command) { Console.Error.WriteLine($"未知命令：{command}。使用 help 查看帮助。"); return 2; }

    private static void PrintHelp() => Console.WriteLine(
        """
        CopyTools 命令行

          copytools tasks [--json] [--data-dir PATH]
          copytools preview --task ID [--details] [--json]
          copytools run --task ID [--allow-overwrite] [--allow-quarantine] [--json]
          copytools queue ID... [--parallel N] [--json]
          copytools incomplete [--json]
          copytools resume --task ID [--json]
          copytools copy --source PATH --target PATH [--mode MODE] [--sha256]
                         [--verify quick|sha256|sampled] [--dry-run]
                         [--allow-overwrite] [--confirm-dangerous] [--parallel N] [--json]

        退出码：0 成功；10 有待执行/恢复内容；12 需要安全确认；20 执行失败；130 已取消。
        """);

    private sealed record CliArguments(
        string? Command,
        IReadOnlyDictionary<string, string?> Options,
        IReadOnlyList<string> Positionals)
    {
        public bool Json => Has("json");
        public bool Has(string name) => Options.ContainsKey(name);
        public string? Get(string name) => Options.GetValueOrDefault(name);
        public string Require(string name) => Get(name) is { Length: > 0 } value ? value : throw new ArgumentException($"缺少 --{name} 参数。");
        public Guid RequireGuid(string name) => ParseGuid(Require(name));
        public int GetInt(string name, int defaultValue, int minimum, int maximum) => Get(name) is null
            ? defaultValue
            : int.TryParse(Get(name), out var value) && value >= minimum && value <= maximum
                ? value : throw new ArgumentException($"--{name} 必须介于 {minimum} 与 {maximum}。 ");

        public static CliArguments Parse(string[] args)
        {
            if (args.Length == 0) return new CliArguments(null, new Dictionary<string, string?>(), []);
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var positionals = new List<string>();
            for (var index = 1; index < args.Length; index++)
            {
                var value = args[index];
                if (!value.StartsWith("--", StringComparison.Ordinal)) { positionals.Add(value); continue; }
                var name = value[2..];
                string? optionValue = null;
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) optionValue = args[++index];
                options[name] = optionValue;
            }
            return new CliArguments(args[0].ToLowerInvariant(), options, positionals);
        }
    }
}
