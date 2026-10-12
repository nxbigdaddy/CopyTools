using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CopyTools.Domain;
using Microsoft.Win32;

namespace CopyTools.Infrastructure;

/// <summary>采集机器信息与已安装应用清单，用于应用状态备份。</summary>
public sealed class AppStateManifestService
{
    public AppStateManifest Capture()
    {
        var applications = new List<InstalledApplication>();
        if (OperatingSystem.IsWindows())
        {
            applications.AddRange(ReadUninstallApplications(RegistryView.Registry64, RegistryHive.LocalMachine));
            applications.AddRange(ReadUninstallApplications(RegistryView.Registry32, RegistryHive.LocalMachine));
            applications.AddRange(ReadUninstallApplications(RegistryView.Registry64, RegistryHive.CurrentUser));
            applications.AddRange(ReadUninstallApplications(RegistryView.Registry32, RegistryHive.CurrentUser));
        }

        return new AppStateManifest(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            DateTime.UtcNow,
            applications
                .GroupBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<InstalledApplication> ReadUninstallApplications(
        RegistryView view,
        RegistryHive hive)
    {
        var result = new List<InstalledApplication>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                return result;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var displayName = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                result.Add(new InstalledApplication(
                    displayName,
                    subKey.GetValue("DisplayVersion") as string,
                    subKey.GetValue("Publisher") as string));
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return result;
    }
}
