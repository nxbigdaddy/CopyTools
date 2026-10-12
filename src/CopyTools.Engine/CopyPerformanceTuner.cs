using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CopyTools.Domain;

namespace CopyTools.Engine;

public static class CopyPerformanceTuner
{
    public static PerformanceOptions Resolve(CopyPlan plan)
    {
        var configured = plan.Request.EffectivePerformance.Normalize();
        if (configured.BackgroundLowImpact)
        {
            var backgroundLimit = configured.SpeedLimitBytesPerSecond ?? 64L * 1024 * 1024;
            return configured with { MaxParallelFiles = 1, SpeedLimitBytesPerSecond = backgroundLimit };
        }
        if (!configured.AutoTune || configured.SpeedLimitBytesPerSecond is not null) return configured;

        var files = plan.Items.Where(item => item.WillCopy && !item.IsDirectory).ToArray();
        if (files.Length < 12) return configured with { MaxParallelFiles = 1 };
        var averageLength = files.Sum(item => (double)item.Length) / files.Length;
        var media = plan.Request.EffectiveSourcePaths
            .Append(plan.Request.TargetDirectory)
            .Select(StorageMediaProbe.Classify)
            .Distinct().ToArray();
        var recommended = media.Contains(StorageMediaKind.Removable) || media.Contains(StorageMediaKind.Rotational)
            ? 1
            : media.Contains(StorageMediaKind.Network)
                ? averageLength <= 4 * 1024 * 1024 ? 6 : 3
                : media.Contains(StorageMediaKind.SolidState)
                    ? averageLength <= 4 * 1024 * 1024 ? 6 : averageLength <= 128 * 1024 * 1024 ? 4 : 2
                    : averageLength <= 1024 * 1024 ? 3 : 2;
        return configured with { MaxParallelFiles = Math.Min(configured.MaxParallelFiles, recommended) };
    }
}

public enum StorageMediaKind { Unknown, SolidState, Rotational, Removable, Network }

public static class StorageMediaProbe
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;

    public static StorageMediaKind Classify(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)) return StorageMediaKind.Network;
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root)) return StorageMediaKind.Unknown;
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network) return StorageMediaKind.Network;
            if (drive.DriveType == DriveType.Removable) return StorageMediaKind.Removable;
            if (!OperatingSystem.IsWindows() || drive.DriveType != DriveType.Fixed) return StorageMediaKind.Unknown;

            using var handle = CreateFile($"\\\\.\\{root.TrimEnd('\\')}", 0,
                FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (handle.IsInvalid) return StorageMediaKind.Unknown;
            var query = new StoragePropertyQuery { PropertyId = 7, QueryType = 0 };
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, ref query,
                    Marshal.SizeOf<StoragePropertyQuery>(), out var descriptor,
                    Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(), out _, IntPtr.Zero))
                return StorageMediaKind.Unknown;
            return descriptor.IncursSeekPenalty ? StorageMediaKind.Rotational : StorageMediaKind.SolidState;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return StorageMediaKind.Unknown;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery { public int PropertyId; public int QueryType; public byte Additional; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)] public bool IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, FileShare share,
        IntPtr securityAttributes, FileMode creationDisposition, uint flags, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode,
        ref StoragePropertyQuery input, int inputSize, out DeviceSeekPenaltyDescriptor output,
        int outputSize, out int bytesReturned, IntPtr overlapped);
}
