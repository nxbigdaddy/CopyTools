using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CopyTools.Infrastructure;

public enum ManagedServiceState
{
    NotInstalled = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7,
    Unknown = 255,
}

[SupportedOSPlatform("windows")]
public sealed partial class WindowsServiceManager
{
    public const string ServiceName = "CopyToolsService";
    public const string DisplayName = "CopyTools 文件同步后台服务";
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceExists = 1073;
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint Delete = 0x00010000;
    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint ServiceAutoStart = 2;
    private const uint ServiceDemandStart = 3;
    private const uint ServiceErrorNormal = 1;
    private const uint ServiceControlStop = 1;
    private const uint ServiceConfigDescription = 1;

    public ManagedServiceState QueryState()
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenServiceHandle(manager, ServiceQueryStatus, allowMissing: true);
        if (service.IsInvalid) return ManagedServiceState.NotInstalled;
        return QueryState(service);
    }

    public void Install(string executablePath, string dataDirectory, bool startAutomatically = true)
    {
        var executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable)) throw new FileNotFoundException("找不到服务可执行文件。", executable);
        var data = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(data);
        var commandLine = $"{QuoteArgument(executable)} --data-dir {QuoteArgument(data)}";
        using var manager = OpenManager(ScManagerConnect | ScManagerCreateService);
        using var service = CreateService(
            manager, ServiceName, DisplayName, 0xF01FF, ServiceWin32OwnProcess,
            startAutomatically ? ServiceAutoStart : ServiceDemandStart,
            ServiceErrorNormal, commandLine, null, IntPtr.Zero, null, null, null);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            throw error == ErrorServiceExists
                ? new InvalidOperationException("CopyTools 后台服务已经安装。")
                : new Win32Exception(error, "安装 CopyTools 后台服务失败。");
        }

        var description = new ServiceDescription
        {
            Description = "在未打开桌面窗口时执行 CopyTools 计划任务和自动触发任务。",
        };
        if (!ChangeServiceConfig2(service, ServiceConfigDescription, ref description))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "设置 CopyTools 服务描述失败。");
    }

    public void Start(TimeSpan? timeout = null)
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenServiceHandle(manager, ServiceStart | ServiceQueryStatus);
        var state = QueryState(service);
        if (state == ManagedServiceState.Running) return;
        if (state != ManagedServiceState.StartPending && !StartService(service, 0, null))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "启动 CopyTools 后台服务失败。");
        WaitForState(service, ManagedServiceState.Running, timeout ?? TimeSpan.FromSeconds(30));
    }

    public void Stop(TimeSpan? timeout = null)
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenServiceHandle(manager, ServiceStop | ServiceQueryStatus);
        var state = QueryState(service);
        if (state == ManagedServiceState.Stopped) return;
        if (state != ManagedServiceState.StopPending && !ControlService(service, ServiceControlStop, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "停止 CopyTools 后台服务失败。");
        WaitForState(service, ManagedServiceState.Stopped, timeout ?? TimeSpan.FromSeconds(30));
    }

    public void Uninstall()
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenServiceHandle(manager, Delete | ServiceStop | ServiceQueryStatus, allowMissing: true);
        if (service.IsInvalid) return;
        var state = QueryState(service);
        if (state is not ManagedServiceState.Stopped and not ManagedServiceState.StopPending)
        {
            if (ControlService(service, ServiceControlStop, out _))
                WaitForState(service, ManagedServiceState.Stopped, TimeSpan.FromSeconds(30));
        }
        else if (state == ManagedServiceState.StopPending)
        {
            WaitForState(service, ManagedServiceState.Stopped, TimeSpan.FromSeconds(30));
        }
        if (!DeleteService(service))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "卸载 CopyTools 后台服务失败。");
    }

    public static string QuoteArgument(string value)
    {
        if (value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')) return value;
        var output = new StringBuilder(value.Length + 2).Append('"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') output.Append('\\', slashes * 2 + 1).Append('"');
            else { output.Append('\\', slashes).Append(character); }
            slashes = 0;
        }
        output.Append('\\', slashes * 2).Append('"');
        return output.ToString();
    }

    private static SafeServiceHandle OpenManager(uint access)
    {
        var handle = OpenSCManager(null, null, access);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "打开 Windows 服务管理器失败。");
        return handle;
    }

    private static SafeServiceHandle OpenServiceHandle(
        SafeServiceHandle manager, uint access, bool allowMissing = false)
    {
        var handle = OpenService(manager, ServiceName, access);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error();
        if (allowMissing && error == ErrorServiceDoesNotExist) return handle;
        handle.Dispose();
        throw new Win32Exception(error, "打开 CopyTools 后台服务失败。");
    }

    private static ManagedServiceState QueryState(SafeServiceHandle service)
    {
        if (!QueryServiceStatus(service, out var status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "查询 CopyTools 服务状态失败。");
        return Enum.IsDefined(typeof(ManagedServiceState), (int)status.CurrentState)
            ? (ManagedServiceState)status.CurrentState : ManagedServiceState.Unknown;
    }

    private static void WaitForState(SafeServiceHandle service, ManagedServiceState expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (QueryState(service) == expected) return;
            Thread.Sleep(200);
        }
        throw new TimeoutException($"等待 CopyTools 服务进入 {expected} 状态超时。");
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string serviceName, uint desiredAccess);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle CreateService(
        SafeServiceHandle manager, string serviceName, string displayName, uint desiredAccess,
        uint serviceType, uint startType, uint errorControl, string binaryPathName,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(SafeServiceHandle service, uint infoLevel, ref ServiceDescription description);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(SafeServiceHandle service, out NativeServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(SafeServiceHandle service, int argumentCount, string[]? arguments);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(SafeServiceHandle service, uint control, out NativeServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(SafeServiceHandle service);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeServiceStatus
    {
        public uint ServiceType; public uint CurrentState; public uint ControlsAccepted;
        public uint Win32ExitCode; public uint ServiceSpecificExitCode; public uint CheckPoint; public uint WaitHint;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription { [MarshalAs(UnmanagedType.LPWStr)] public string Description; }

    private sealed class SafeServiceHandle : SafeHandle
    {
        private SafeServiceHandle() : base(IntPtr.Zero, true) { }
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);
}
