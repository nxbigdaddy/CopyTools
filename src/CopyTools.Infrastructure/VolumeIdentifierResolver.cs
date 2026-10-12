using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public static partial class VolumeIdentifierResolver
{
    public static string GetStableIdentifier(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new ArgumentException("无法解析卷根路径。", nameof(path));
        var buffer = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPoint(EnsureTrailingSlash(root), buffer, buffer.Capacity))
        {
            throw new InvalidOperationException($"无法读取卷标识：{Marshal.GetLastWin32Error()}。");
        }

        return buffer.ToString();
    }

    public static string ResolveMountPoint(string stableIdentifier)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (string.Equals(GetStableIdentifier(drive.RootDirectory.FullName), stableIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    return drive.RootDirectory.FullName;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        throw new DriveNotFoundException($"未找到卷：{stableIdentifier}");
    }

    public static string ToStablePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("无法解析卷根路径。", nameof(path));
        if (root.StartsWith("\\\\", StringComparison.Ordinal)) return fullPath;
        var stableRoot = GetStableIdentifier(fullPath);
        var relative = Path.GetRelativePath(root, fullPath);
        return relative == "." ? stableRoot : Path.Combine(stableRoot, relative);
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('\\') ? value : value + "\\";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint,
        StringBuilder volumeName,
        int bufferLength);
}
