using System.Security.Cryptography;
using System.Text.Json;
using CopyTools.Domain;
using System.Runtime.Versioning;

namespace CopyTools.Engine;

public sealed class SafeFileCopier
{
    private const int DefaultBufferSize = 1024 * 1024;
    private readonly DeltaFileCopier _deltaFileCopier;

    public SafeFileCopier(DeltaFileCopier? deltaFileCopier = null)
    {
        _deltaFileCopier = deltaFileCopier ?? new DeltaFileCopier();
    }

    public async Task CopyAsync(
        string sourcePath,
        string targetPath,
        DateTime sourceLastWriteTimeUtc,
        FileAttributes sourceAttributes,
        bool allowOverwrite,
        VerificationMode verificationMode,
        IProgress<long>? byteProgress,
        CancellationToken cancellationToken,
        CopyExecutionControl? executionControl = null,
        PerformanceOptions? performanceOptions = null,
        FileSecurityOptions? securityOptions = null,
        TransferMode transferMode = TransferMode.Full)
    {
        var performance = (performanceOptions ?? PerformanceOptions.Default).Normalize();
        var bufferSize = performance.BufferSizeBytes;
        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("无法确定目标文件所在目录。");
        Directory.CreateDirectory(targetDirectory);

        var temporaryPath = CreateTemporaryPath(targetPath);
        var metadataPath = temporaryPath + ".json";

        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            if (transferMode == TransferMode.Delta && File.Exists(targetPath))
            {
                await _deltaFileCopier.ReconstructAsync(
                    sourcePath,
                    targetPath,
                    temporaryPath,
                    byteProgress,
                    cancellationToken,
                    executionControl,
                    performance);
            }
            else
            {
                await CopyFullAsync(
                    sourcePath,
                    temporaryPath,
                    metadataPath,
                    sourceInfo,
                    sourceLastWriteTimeUtc,
                    bufferSize,
                    performance,
                    byteProgress,
                    cancellationToken,
                    executionControl);
            }

            var temporaryInfo = new FileInfo(temporaryPath);
            if (temporaryInfo.Length != sourceInfo.Length)
            {
                throw new IOException("复制后的临时文件大小与源文件不一致。");
            }

            if (verificationMode is VerificationMode.Sha256 or VerificationMode.SampledSha256)
            {
                var sourceHash = await ComputeVerificationHashAsync(sourcePath, verificationMode, cancellationToken);
                var temporaryHash = await ComputeVerificationHashAsync(temporaryPath, verificationMode, cancellationToken);
                if (!sourceHash.AsSpan().SequenceEqual(temporaryHash))
                {
                    TryDelete(temporaryPath);
                    TryDelete(metadataPath);
                    throw new IOException("内容校验失败，复制内容与源文件不一致。");
                }
            }

            File.SetLastWriteTimeUtc(temporaryPath, sourceLastWriteTimeUtc);
            CommitTemporaryFile(temporaryPath, targetPath, allowOverwrite);
            TryDelete(metadataPath);
            var attributesToPreserve = sourceAttributes
                & (FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System);
            File.SetAttributes(targetPath, attributesToPreserve);
            if ((securityOptions ?? new FileSecurityOptions()).PreserveNtfsPermissions)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("NTFS 权限保留仅支持 Windows。");
                }
                PreserveNtfsPermissions(sourcePath, targetPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            throw;
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(metadataPath);
            throw;
        }
    }

    private static async Task CopyFullAsync(
        string sourcePath,
        string temporaryPath,
        string metadataPath,
        FileInfo sourceInfo,
        DateTime sourceLastWriteTimeUtc,
        int bufferSize,
        PerformanceOptions performance,
        IProgress<long>? byteProgress,
        CancellationToken cancellationToken,
        CopyExecutionControl? executionControl)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var resumeOffset = PrepareResume(
            sourceInfo, temporaryPath, metadataPath, sourceLastWriteTimeUtc);
        await using var target = new FileStream(
            temporaryPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        source.Position = resumeOffset;
        target.Position = resumeOffset;
        target.SetLength(resumeOffset);
        if (resumeOffset > 0)
        {
            byteProgress?.Report(resumeOffset);
        }

        var buffer = new byte[bufferSize];
        var throttle = performance.SpeedLimitBytesPerSecond is null
            ? null
            : new TransferThrottle(performance.SpeedLimitBytesPerSecond.Value);
        while (true)
        {
            if (executionControl is not null)
            {
                await executionControl.WaitIfPausedAsync(cancellationToken);
            }

            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            byteProgress?.Report(bytesRead);
            if (throttle is not null)
            {
                await throttle.DelayIfNeededAsync(bytesRead, cancellationToken);
            }
        }

        await target.FlushAsync(cancellationToken);
        target.Close();
    }

    private static string CreateTemporaryPath(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        var fileName = Path.GetFileName(targetPath);
        return Path.Combine(directory, $".{fileName}.copytools-partial");
    }

    private static long PrepareResume(
        FileInfo source,
        string temporaryPath,
        string metadataPath,
        DateTime expectedLastWriteTimeUtc)
    {
        ResumeMetadata? metadata = null;
        try
        {
            if (File.Exists(metadataPath))
            {
                metadata = JsonSerializer.Deserialize<ResumeMetadata>(File.ReadAllText(metadataPath));
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        var valid = metadata is not null
            && string.Equals(metadata.SourcePath, source.FullName, StringComparison.OrdinalIgnoreCase)
            && metadata.Length == source.Length
            && Math.Abs((metadata.LastWriteTimeUtc - expectedLastWriteTimeUtc).TotalSeconds) <= 2
            && File.Exists(temporaryPath)
            && new FileInfo(temporaryPath).Length <= source.Length;
        if (!valid)
        {
            TryDelete(temporaryPath);
            TryDelete(metadataPath);
            WriteResumeMetadata(metadataPath, new ResumeMetadata(
                source.FullName, source.Length, expectedLastWriteTimeUtc));
            return 0;
        }

        return new FileInfo(temporaryPath).Length;
    }

    private static void WriteResumeMetadata(string path, ResumeMetadata metadata)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(metadata));
            File.Move(temporary, path, true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void CommitTemporaryFile(
        string temporaryPath,
        string targetPath,
        bool allowOverwrite)
    {
        if (!allowOverwrite)
        {
            File.Move(temporaryPath, targetPath, overwrite: false);
            return;
        }

        if (File.Exists(targetPath))
        {
            File.Replace(temporaryPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, targetPath, overwrite: false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<byte[]> ComputeVerificationHashAsync(
        string path,
        VerificationMode verificationMode,
        CancellationToken cancellationToken) =>
        verificationMode == VerificationMode.SampledSha256
            ? await ContentHasher.ComputeSampledSha256Async(path, cancellationToken)
            : await ComputeSha256Async(path, cancellationToken);

    private static async Task<byte[]> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            DefaultBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static void PreserveNtfsPermissions(string sourcePath, string targetPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("NTFS 权限保留仅支持 Windows。");
        var info = new FileInfo(sourcePath);
        var security = info.GetAccessControl(
            System.Security.AccessControl.AccessControlSections.Access
            | System.Security.AccessControl.AccessControlSections.Owner
            | System.Security.AccessControl.AccessControlSections.Group);
        new FileInfo(targetPath).SetAccessControl(security);
    }

    private sealed class TransferThrottle(long bytesPerSecond)
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private long _bytes;

        public async Task DelayIfNeededAsync(int addedBytes, CancellationToken cancellationToken)
        {
            _bytes += addedBytes;
            var expectedSeconds = _bytes / (double)bytesPerSecond;
            var delay = TimeSpan.FromSeconds(expectedSeconds) - _stopwatch.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private sealed record ResumeMetadata(string SourcePath, long Length, DateTime LastWriteTimeUtc);
}
