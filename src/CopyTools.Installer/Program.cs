using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace CopyTools.Installer;

internal static class Program
{
    private const string ProductName = "CopyTools";
    private static readonly string DefaultInstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);

    [STAThread]
    private static int Main(string[] args)
    {
        var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--verify-payload", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--validate-install-dir", StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetOption(args, "--validate-install-dir") is { } pathToValidate)
            {
                _ = ValidateInstallDirectory(pathToValidate, requireInstalledMarker: false);
                return 0;
            }
            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                if (!quiet && MessageBox(IntPtr.Zero, "卸载 CopyTools？用户任务、日志和凭据将保留。", "CopyTools 卸载",
                        0x00000004 | 0x00000030) != 6) return 0;
                Uninstall(GetOption(args, "--install-dir") ?? DefaultInstallDirectory);
                return 0;
            }

            var installDirectory = ValidateInstallDirectory(GetOption(args, "--install-dir") ?? DefaultInstallDirectory,
                requireInstalledMarker: false);
            if (args.Contains("--verify-payload", StringComparer.OrdinalIgnoreCase))
            {
                VerifyPayload(); return 0;
            }
            var quietInstall = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
            if (!quietInstall && MessageBox(IntPtr.Zero,
                    $"将 CopyTools 安装到：\n{installDirectory}\n\n是否继续？", "CopyTools 安装", 0x00000004 | 0x00000040) != 6)
                return 0;
            Install(installDirectory,
                createShortcut: !args.Contains("--no-shortcuts", StringComparer.OrdinalIgnoreCase),
                registerUninstaller: !args.Contains("--no-register", StringComparer.OrdinalIgnoreCase));
            if (!quietInstall)
            {
                MessageBox(IntPtr.Zero, "CopyTools 安装完成。", "CopyTools", 0x00000000 | 0x00000040);
                Process.Start(new ProcessStartInfo(Path.Combine(installDirectory, "CopyTools.exe")) { UseShellExecute = true });
            }
            return 0;
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "copytools-installer-error.log"), exception.ToString()); } catch (IOException) { }
            if (!quiet) MessageBox(IntPtr.Zero, exception.Message, "CopyTools 安装失败", 0x00000000 | 0x00000010);
            return 1;
        }
    }

    private static void Install(string installDirectory, bool createShortcut, bool registerUninstaller)
    {
        Directory.CreateDirectory(installDirectory);
        using var payload = typeof(Program).Assembly.GetManifestResourceStream("CopyToolsPayload")
            ?? throw new InvalidDataException("安装器不包含 CopyToolsPayload 资源。请使用发布脚本生成安装器。");
        var staging = Path.Combine(Path.GetTempPath(), $"copytools-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            ZipFile.ExtractToDirectory(payload, staging, overwriteFiles: true);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, file);
                var target = Path.GetFullPath(Path.Combine(installDirectory, relative));
                if (!target.StartsWith(installDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包包含越界路径。");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
            File.Copy(Environment.ProcessPath!, Path.Combine(installDirectory, "Uninstall-CopyTools.exe"), true);
            if (createShortcut) CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "CopyTools.lnk"),
                Path.Combine(installDirectory, "CopyTools.exe"), installDirectory);
            if (registerUninstaller) WriteUninstallRegistration(installDirectory);
        }
        finally { TryDeleteTemporaryDirectory(staging); }
    }

    private static void VerifyPayload()
    {
        using var payload = typeof(Program).Assembly.GetManifestResourceStream("CopyToolsPayload")
            ?? throw new InvalidDataException("安装器不包含 CopyToolsPayload 资源。");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!entries.Contains("CopyTools.exe") || !entries.Contains("service/CopyTools.Service.exe")
            || !entries.Contains("cli/copytools.exe"))
            throw new InvalidDataException("安装器负载缺少桌面、服务或 CLI 程序。");
    }

    private static void Uninstall(string installDirectory)
    {
        installDirectory = ValidateInstallDirectory(installDirectory, requireInstalledMarker: true);
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "CopyTools.lnk");
        if (File.Exists(shortcut)) File.Delete(shortcut);
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                   @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
            key?.DeleteSubKeyTree("CopyTools", throwOnMissingSubKey: false);
        var command = $"/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q {Quote(installDirectory)}";
        Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false });
    }

    internal static string ValidateInstallDirectory(string path, bool requireInstalledMarker)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("安装目录不能为空。");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? string.Empty);
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("拒绝在磁盘根目录安装或卸载。");
        var userProfile = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var windows = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Path.TrimEndingDirectorySeparator);
        if (string.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, windows, StringComparison.OrdinalIgnoreCase)
            || programFiles.Any(value => string.Equals(fullPath, value, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("拒绝把用户目录或系统程序根目录作为 CopyTools 安装目录。");
        if (requireInstalledMarker && !File.Exists(Path.Combine(fullPath, "CopyTools.exe"))
            && !File.Exists(Path.Combine(fullPath, "Uninstall-CopyTools.exe")))
            throw new InvalidOperationException("目标目录中没有 CopyTools 安装标记，已拒绝递归删除。");
        return fullPath;
    }

    private static void CreateShortcut(string path, string target, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("无法创建开始菜单快捷方式。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = target; shortcut.WorkingDirectory = workingDirectory; shortcut.Description = "CopyTools 文件复制与同步";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell);
    }

    private static void WriteUninstallRegistration(string installDirectory)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CopyTools", writable: true);
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
        key.SetValue("DisplayName", "CopyTools 文件复制与同步"); key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "CopyTools"); key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", Path.Combine(installDirectory, "CopyTools.exe"));
        key.SetValue("UninstallString", $"{Quote(Path.Combine(installDirectory, "Uninstall-CopyTools.exe"))} --uninstall --install-dir {Quote(installDirectory)}");
        key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord); key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static void TryDeleteTemporaryDirectory(string directory)
    {
        // 病毒扫描器或刚结束的文件复制可能会短暂占用自包含运行时 DLL。
        // 安装已经成功时，临时目录清理失败不应把安装结果改成失败。
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt < 5) Thread.Sleep(250 * attempt);
            }
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
