using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class WindowsVssSnapshotService
{
    private readonly string _diskshadowPath;

    public WindowsVssSnapshotService(string? diskshadowPath = null)
    {
        _diskshadowPath = diskshadowPath ?? Path.Combine(Environment.SystemDirectory, "diskshadow.exe");
    }

    public bool IsAvailable => File.Exists(_diskshadowPath);

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public async Task<VssSnapshotLease> CreateAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new FileNotFoundException("Windows diskshadow.exe 不可用。", _diskshadowPath);
        if (!IsAdministrator) throw new UnauthorizedAccessException("VSS 一致性快照需要以管理员身份运行 CopyTools。");
        var fullSource = Path.GetFullPath(sourcePath);
        var root = Path.GetPathRoot(fullSource)
            ?? throw new ArgumentException("无法解析 VSS 源卷。", nameof(sourcePath));
        if (root.StartsWith("\\\\", StringComparison.Ordinal))
            throw new NotSupportedException("VSS 只能为本机卷创建快照，不能用于 UNC 路径。");
        var drive = FindFreeDriveLetter();
        var alias = $"CopyTools{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(Path.GetTempPath(), $"copytools-vss-{Guid.NewGuid():N}.dsh");
        var script = BuildCreateScript(root, drive, alias);
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);
        try
        {
            await RunDiskshadowAsync(scriptPath, cancellationToken);
            var mountedRoot = $"{drive}:\\";
            if (!Directory.Exists(mountedRoot))
                throw new IOException("VSS 快照创建后未能访问暴露的驱动器。");
            var relative = Path.GetRelativePath(root, fullSource);
            var snapshotPath = relative == "." ? mountedRoot : Path.Combine(mountedRoot, relative);
            return new VssSnapshotLease(this, drive, snapshotPath);
        }
        catch
        {
            TryDelete(scriptPath);
            throw;
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    internal async Task ReleaseAsync(char drive)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"copytools-vss-cleanup-{Guid.NewGuid():N}.dsh");
        await File.WriteAllTextAsync(scriptPath, BuildCleanupScript(drive));
        try { await RunDiskshadowAsync(scriptPath, CancellationToken.None); }
        finally { TryDelete(scriptPath); }
    }

    internal static string BuildCreateScript(string volumeRoot, char drive, string alias) =>
        $"SET CONTEXT PERSISTENT NOWRITERS{Environment.NewLine}"
        + $"BEGIN BACKUP{Environment.NewLine}"
        + $"ADD VOLUME {volumeRoot} ALIAS {alias}{Environment.NewLine}"
        + $"CREATE{Environment.NewLine}"
        + $"EXPOSE %{alias}% {drive}:{Environment.NewLine}"
        + $"END BACKUP{Environment.NewLine}"
        + $"EXIT{Environment.NewLine}";

    internal static string BuildCleanupScript(char drive) =>
        $"DELETE SHADOWS EXPOSED {drive}:{Environment.NewLine}"
        + $"EXIT{Environment.NewLine}";

    private async Task RunDiskshadowAsync(string scriptPath, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_diskshadowPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add(scriptPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Windows VSS 工具。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0 || output.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            throw new IOException($"VSS 操作失败（退出码 {process.ExitCode}）：{Sanitize(error.Length > 0 ? error : output)}");
    }

    private static char FindFreeDriveLetter()
    {
        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        for (var value = 'Z'; value >= 'D'; value--)
            if (!used.Contains(value)) return value;
        throw new IOException("没有可用于暴露 VSS 快照的空闲驱动器号。");
    }

    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim() is { Length: > 1000 } text ? text[..1000] : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

[SupportedOSPlatform("windows")]
public sealed class VssSnapshotLease : IAsyncDisposable
{
    private WindowsVssSnapshotService? _owner;
    private readonly char _drive;

    internal VssSnapshotLease(WindowsVssSnapshotService owner, char drive, string snapshotPath)
    {
        _owner = owner;
        _drive = drive;
        SnapshotPath = snapshotPath;
    }

    public string SnapshotPath { get; }

    public async ValueTask DisposeAsync()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null) await owner.ReleaseAsync(_drive);
    }
}
