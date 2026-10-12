using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class TaskTransferService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ICopyToolsRepository _repository;

    public TaskTransferService(ICopyToolsRepository repository)
    {
        _repository = repository;
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var tasks = await _repository.GetTasksAsync(cancellationToken);
        var schedules = await _repository.GetSchedulesAsync(cancellationToken);
        var bundle = new TaskExportBundle(2, DateTime.UtcNow, tasks, schedules)
        {
            AutoTriggerRules = await _repository.GetAutoTriggerRulesAsync(cancellationToken),
            ExecutionOptions = await _repository.GetAllTaskExecutionOptionsAsync(cancellationToken),
        };
        await using var stream = new FileStream(
            filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, bundle, Options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<int> ImportAsync(
        string filePath,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bundle = await JsonSerializer.DeserializeAsync<TaskExportBundle>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("任务配置文件为空或格式无效。");
        if (bundle.FormatVersion is not (1 or 2))
        {
            throw new InvalidDataException($"不支持的任务配置版本：{bundle.FormatVersion}。");
        }

        var existing = (await _repository.GetTasksAsync(cancellationToken))
            .Select(task => task.Id)
            .ToHashSet();
        var imported = 0;
        var idMap = new Dictionary<Guid, Guid>();
        foreach (var task in bundle.Tasks)
        {
            var id = existing.Contains(task.Id) && !replaceExisting ? Guid.NewGuid() : task.Id;
            idMap[task.Id] = id;
            var request = task.Request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
                ? task.Request with { SynchronizationId = id }
                : task.Request;
            await _repository.SaveTaskAsync(
                task with
                {
                    Id = id,
                    Name = id == task.Id ? task.Name : $"{task.Name}（导入）",
                    Request = request,
                    UpdatedAtUtc = DateTime.UtcNow,
                },
                cancellationToken);
            imported++;
        }

        foreach (var schedule in bundle.Schedules)
        {
            if (idMap.TryGetValue(schedule.TaskId, out var mappedTaskId))
            {
                await _repository.SaveScheduleAsync(
                    schedule with { TaskId = mappedTaskId, NextRunAtUtc = null },
                    cancellationToken);
            }
        }

        foreach (var rule in bundle.AutoTriggerRules)
        {
            if (idMap.TryGetValue(rule.TaskId, out var mappedTaskId))
            {
                await _repository.SaveAutoTriggerRuleAsync(rule with { TaskId = mappedTaskId }, cancellationToken);
            }
        }

        foreach (var options in bundle.ExecutionOptions)
        {
            if (!idMap.TryGetValue(options.TaskId, out var mappedTaskId)) continue;
            var dependencies = options.EffectiveDependencies
                .Select(id => idMap.GetValueOrDefault(id, id))
                .Where(id => id != mappedTaskId)
                .Distinct()
                .ToArray();
            await _repository.SaveTaskExecutionOptionsAsync(
                options with { TaskId = mappedTaskId, Dependencies = dependencies }, cancellationToken);
        }

        return imported;
    }
}
