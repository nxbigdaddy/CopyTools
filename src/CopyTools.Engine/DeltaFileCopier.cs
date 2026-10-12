using CopyTools.Domain;

namespace CopyTools.Engine;

/// <summary>按对齐块比较源与已有目标，仅把发生变化的块写入临时文件。</summary>
public sealed class DeltaFileCopier
{
    private const int MinimumBlockSize = 64 * 1024;
    private const int MaximumBlockSize = 4 * 1024 * 1024;
    private const int DefaultBlockSize = 1024 * 1024;

    public async Task<DeltaTransferResult> ReconstructAsync(
        string sourcePath,
        string targetPath,
        string temporaryPath,
        IProgress<long>? byteProgress = null,
        CancellationToken cancellationToken = default,
        CopyExecutionControl? executionControl = null,
        PerformanceOptions? performanceOptions = null)
    {
        var sourceInfo = new FileInfo(sourcePath);
        var targetLength = new FileInfo(targetPath).Length;
        var blockSize = ComputeBlockSize(sourceInfo.Length);

        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            (int)blockSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(
            targetPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            (int)blockSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
            (int)blockSize, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        var sourceBuffer = new byte[blockSize];
        var targetBuffer = new byte[blockSize];
        var throttle = BuildThrottle(performanceOptions);
        long reusedBytes = 0;
        long changedBytes = 0;
        long position = 0;

        while (position < sourceInfo.Length)
        {
            if (executionControl is not null)
            {
                await executionControl.WaitIfPausedAsync(cancellationToken);
            }

            var sourceCount = await ReadBlockAsync(source, sourceBuffer, cancellationToken);
            if (sourceCount == 0)
            {
                break;
            }

            var targetCount = position < targetLength
                ? await ReadBlockAsync(target, targetBuffer, cancellationToken)
                : 0;
            var identical = sourceCount == targetCount
                && sourceBuffer.AsSpan(0, sourceCount).SequenceEqual(targetBuffer.AsSpan(0, targetCount));
            if (identical)
            {
                await output.WriteAsync(targetBuffer.AsMemory(0, targetCount), cancellationToken);
                reusedBytes += targetCount;
            }
            else
            {
                await output.WriteAsync(sourceBuffer.AsMemory(0, sourceCount), cancellationToken);
                changedBytes += sourceCount;
            }

            position += sourceCount;
            byteProgress?.Report(position);
            if (throttle is not null)
            {
                await throttle.DelayIfNeededAsync(sourceCount, cancellationToken);
            }
        }

        output.SetLength(sourceInfo.Length);
        await output.FlushAsync(cancellationToken);
        output.Close();
        return new DeltaTransferResult(blockSize, reusedBytes, changedBytes);
    }

    private static long ComputeBlockSize(long sourceLength)
    {
        if (sourceLength <= 0)
        {
            return DefaultBlockSize;
        }

        return Math.Clamp(Math.Max(MinimumBlockSize, sourceLength / 64), MinimumBlockSize, MaximumBlockSize);
    }

    private static async Task<int> ReadBlockAsync(FileStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static TransferThrottle? BuildThrottle(PerformanceOptions? options)
    {
        var performance = (options ?? PerformanceOptions.Default).Normalize();
        return performance.SpeedLimitBytesPerSecond is null
            ? null
            : new TransferThrottle(performance.SpeedLimitBytesPerSecond.Value);
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
}
