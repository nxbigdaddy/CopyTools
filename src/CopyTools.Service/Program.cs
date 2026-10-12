using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CopyTools.Application;
using CopyTools.Engine;
using CopyTools.Infrastructure;

[assembly: SupportedOSPlatform("windows")]

return await ServiceEntry.RunAsync(args);

[SupportedOSPlatform("windows")]
internal static class ServiceEntry
{
    private const int ErrorFailedServiceControllerConnect = 1063;

    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("CopyTools Service 仅支持 Windows。");
            return 2;
        }

        var dataDirectory = GetOption(args, "--data-dir") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CopyTools");
        var serviceManager = new WindowsServiceManager();
        if (args.Contains("--status", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(serviceManager.QueryState());
            return 0;
        }
        if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
        {
            serviceManager.Install(Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定服务程序路径。"), dataDirectory);
            serviceManager.Start();
            Console.WriteLine($"CopyTools Service 已安装并启动：{Path.GetFullPath(dataDirectory)}");
            return 0;
        }
        if (args.Contains("--start", StringComparer.OrdinalIgnoreCase))
        {
            serviceManager.Start(); Console.WriteLine("CopyTools Service 已启动。"); return 0;
        }
        if (args.Contains("--stop", StringComparer.OrdinalIgnoreCase))
        {
            serviceManager.Stop(); Console.WriteLine("CopyTools Service 已停止。"); return 0;
        }
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            serviceManager.Uninstall(); Console.WriteLine("CopyTools Service 已卸载，配置数据未删除。"); return 0;
        }
        if (GetOption(args, "--import-config") is { } configurationPath)
        {
            Directory.CreateDirectory(dataDirectory);
            var repository = new SqliteCopyToolsRepository(Path.Combine(dataDirectory, "copytools.db"));
            await repository.InitializeAsync();
            var settings = new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
            var result = await new ConfigurationBackupService(repository, settings)
                .ImportAsync(configurationPath, replaceExisting: true);
            Console.WriteLine($"服务配置已更新：任务 {result.Tasks}，连接 {result.StorageConnections}，通知 {result.NotificationChannels}，模板 {result.Templates}。请重启服务使配置生效。");
            return 0;
        }
        if (args.Contains("--check", StringComparer.OrdinalIgnoreCase))
        {
            await using var runtime = new CopyToolsServiceRuntime(dataDirectory);
            await runtime.CheckAsync();
            Console.WriteLine($"CopyTools Service 检查通过：{Path.GetFullPath(dataDirectory)}");
            return 0;
        }

        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("CopyTools Service 控制台模式已启动；按 Ctrl+C 停止。");
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            await using var runtime = new CopyToolsServiceRuntime(dataDirectory);
            await runtime.RunAsync(cancellation.Token);
            return 0;
        }

        try
        {
            return WindowsServiceDispatcher.Run(dataDirectory);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorFailedServiceControllerConnect)
        {
            Console.Error.WriteLine("当前进程不是由 Windows 服务控制管理器启动。调试时请使用 --console。");
            return 2;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        return null;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class CopyToolsServiceRuntime(string dataDirectory) : IAsyncDisposable
{
    private readonly string _dataDirectory = Path.GetFullPath(dataDirectory);
    private TaskSchedulerService? _scheduler;
    private AutoTriggerService? _autoTriggers;
    private string _logLevel = "Information";
    private int _logRetentionDays = 180;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dataDirectory);
        var repository = new SqliteCopyToolsRepository(Path.Combine(_dataDirectory, "copytools.db"));
        await repository.InitializeAsync(cancellationToken);
        var settings = await new JsonSettingsStore(Path.Combine(_dataDirectory, "settings.json")).LoadAsync(cancellationToken);
        _logLevel = settings.LogLevel;
        _logRetentionDays = settings.LogRetentionDays;
        _ = await repository.GetSchedulesAsync(cancellationToken);
        _ = await repository.GetAutoTriggerRulesAsync(cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        var repository = new SqliteCopyToolsRepository(Path.Combine(_dataDirectory, "copytools.db"));
        await repository.InitializeAsync(cancellationToken);
        var settings = await new JsonSettingsStore(Path.Combine(_dataDirectory, "settings.json")).LoadAsync(cancellationToken);
        _logLevel = settings.LogLevel;
        _logRetentionDays = settings.LogRetentionDays;
        var workflow = new CopyWorkflow(new CopyPlanService(snapshotStore: repository));
        var credentials = new WindowsCredentialStore(Path.Combine(_dataDirectory, "credentials"));
        var settingsStore = new JsonSettingsStore(Path.Combine(_dataDirectory, "settings.json"));
        var runner = new ScheduledTaskRunner(
            repository, workflow, new TaskNotificationService(repository, credentials), settingsStore);
        _scheduler = new TaskSchedulerService(repository, runner);
        _autoTriggers = new AutoTriggerService(repository, runner);
        _scheduler.RunCompleted += (_, result) => WriteServiceLog(result.TaskName, result.Status, result.Message);
        _autoTriggers.RunCompleted += (_, result) => WriteServiceLog(result.TaskName, result.Status, result.Message);
        await _scheduler.StartAsync(cancellationToken);
        await _autoTriggers.ConfigureAsync(await repository.GetAutoTriggerRulesAsync(cancellationToken), cancellationToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_autoTriggers is not null) await _autoTriggers.DisposeAsync();
        if (_scheduler is not null) await _scheduler.DisposeAsync();
    }

    private void WriteServiceLog(string taskName, string status, string? message)
    {
        var severity = status == "成功" ? 1 : status is "等待人工确认" or "已取消" ? 2 : 3;
        var threshold = _logLevel switch { "Debug" => 0, "Information" => 1, "Warning" => 2, "Error" => 3, _ => 1 };
        if (severity < threshold) return;
        try
        {
            var logDirectory = Path.Combine(_dataDirectory, "service-logs");
            Directory.CreateDirectory(logDirectory);
            CleanupOldLogs(logDirectory);
            var sanitized = Redact(message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            File.AppendAllText(
                Path.Combine(logDirectory, $"service-{DateTime.UtcNow:yyyyMMdd}.log"),
                $"{DateTime.UtcNow:O}\t{status}\t{taskName}\t{sanitized}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CleanupOldLogs(string directory)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-_logRetentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, "service-*.log"))
            if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
    }

    private static string Redact(string value)
    {
        var output = System.Text.RegularExpressions.Regex.Replace(value,
            "(?i)(access[_-]?token|refresh[_-]?token|token|api[_-]?key|secret|password)=([^&\\s]+)",
            "$1=[已隐藏]");
        output = System.Text.RegularExpressions.Regex.Replace(output,
            "(?i)Bearer\\s+[A-Za-z0-9._~+/-]+=*", "Bearer [已隐藏]");
        return output.Length <= 2000 ? output : output[..2000];
    }
}

[SupportedOSPlatform("windows")]
internal static partial class WindowsServiceDispatcher
{
    private const string ServiceName = "CopyToolsService";
    private const int ServiceWin32OwnProcess = 0x10;
    private const int ServiceStartPending = 2;
    private const int ServiceStopPending = 3;
    private const int ServiceRunning = 4;
    private const int ServiceStopped = 1;
    private const int ServiceAcceptStop = 1;
    private const int ServiceAcceptShutdown = 4;
    private const int ServiceControlStop = 1;
    private const int ServiceControlShutdown = 5;
    private static readonly ServiceMainCallback ServiceMainDelegate = ServiceMain;
    private static readonly HandlerCallback HandlerDelegate = ControlHandler;
    private static CancellationTokenSource? _cancellation;
    private static IntPtr _statusHandle;
    private static string _dataDirectory = string.Empty;
    private static uint _checkpoint;

    public static int Run(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        var table = new[]
        {
            new ServiceTableEntry { ServiceName = ServiceName, ServiceMain = ServiceMainDelegate },
            new ServiceTableEntry(),
        };
        if (!StartServiceCtrlDispatcher(table))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return 0;
    }

    private static void ServiceMain(int argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, HandlerDelegate, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) return;
        _cancellation = new CancellationTokenSource();
        SetStatus(ServiceStartPending, 0, 15_000);
        try
        {
            using var runtime = new AsyncDisposableScope(new CopyToolsServiceRuntime(_dataDirectory));
            var runningTask = runtime.Value.RunAsync(_cancellation.Token);
            SetStatus(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown, 0);
            runningTask.GetAwaiter().GetResult();
            SetStatus(ServiceStopPending, 0, 15_000);
        }
        catch (Exception exception)
        {
            WriteEmergencyLog(exception);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetStatus(ServiceStopped, 0, 0);
        }
    }

    private static int ControlHandler(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        if (control is ServiceControlStop or ServiceControlShutdown)
        {
            SetStatus(ServiceStopPending, 0, 15_000);
            _cancellation?.Cancel();
        }
        return 0;
    }

    private static void SetStatus(int state, int acceptedControls, int waitHint)
    {
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = acceptedControls,
            Win32ExitCode = 0,
            ServiceSpecificExitCode = 0,
            CheckPoint = state is ServiceStartPending or ServiceStopPending ? ++_checkpoint : 0,
            WaitHint = waitHint,
        };
        _ = SetServiceStatus(_statusHandle, ref status);
    }

    private static void WriteEmergencyLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.AppendAllText(Path.Combine(_dataDirectory, "service-error.log"),
                $"{DateTime.UtcNow:O}\t{exception.GetType().Name}\t{exception.Message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerCallback handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainCallback(int argumentCount, IntPtr arguments);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int HandlerCallback(int control, int eventType, IntPtr eventData, IntPtr context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServiceName;
        public ServiceMainCallback? ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public uint CheckPoint;
        public int WaitHint;
    }

    private sealed class AsyncDisposableScope(CopyToolsServiceRuntime value) : IDisposable
    {
        public CopyToolsServiceRuntime Value { get; } = value;
        public void Dispose() => Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
