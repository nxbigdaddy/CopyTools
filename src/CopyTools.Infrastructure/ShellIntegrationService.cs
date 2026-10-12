using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CopyTools.Infrastructure;

public sealed record ShellMenuEntry(string RegistryPath, string DisplayName, string Command);

/// <summary>向资源管理器注册"用 CopyTools 复制到…"右键菜单与"发送到"入口。</summary>
[SupportedOSPlatform("windows")]
public sealed class ShellIntegrationService
{
    private const string DisplayName = "用 CopyTools 复制到…";
    private const string DirectoryMenu = @"Software\Classes\Directory\shell\CopyTools.CopyTo";
    private const string FileMenu = @"Software\Classes\*\shell\CopyTools.CopyTo";

    public IReadOnlyList<ShellMenuEntry> BuildEntries(string appPath)
    {
        var command = $"{Quote(appPath)} \"%1\"";
        return
        [
            new ShellMenuEntry(DirectoryMenu, DisplayName, command),
            new ShellMenuEntry(FileMenu, DisplayName, command),
        ];
    }

    public void Install(string appPath)
    {
        foreach (var entry in BuildEntries(appPath))
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.RegistryPath);
            key.SetValue(string.Empty, entry.DisplayName);
            key.SetValue("Icon", appPath);
            using var commandKey = key.CreateSubKey("command");
            commandKey.SetValue(string.Empty, entry.Command);
        }
    }

    public void Uninstall()
    {
        foreach (var path in new[] { DirectoryMenu, FileMenu })
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (System.IO.IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
