using System.Security.Cryptography;

namespace CopyTools.Engine;

/// <summary>集中提供内容哈希计算，覆盖整文件与采样两种粒度。</summary>
public static class ContentHasher
{
    /// <summary>采样块大小：对超大文件仅读取首、中、尾三个区域各最多此字节数。</summary>
    public const int SampleBlockSize = 256 * 1024;

    public static string ComputeSha256Hex(string path)
    {
        using var stream = OpenSequential(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string ComputeSampledSha256Hex(string path)
    {
        using var stream = OpenSequential(path);
        return Convert.ToHexString(ComputeSampledSha256(stream)).ToLowerInvariant();
    }

    public static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenAsync(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    public static async Task<string> ComputeSampledSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        var hash = await ComputeSampledSha256Async(path, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<byte[]> ComputeSampledSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenAsync(path);
        return await ComputeSampledSha256Async(stream, cancellationToken);
    }

    /// <summary>对流执行采样哈希：小于等于一个采样块时退化为整文件哈希。</summary>
    public static byte[] ComputeSampledSha256(Stream stream)
    {
        var length = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SampleBlockSize];
        if (length <= SampleBlockSize)
        {
            AppendAll(stream, hash, buffer);
        }
        else
        {
            var middle = length / 2;
            AppendRegion(stream, hash, buffer, 0, SampleBlockSize);
            AppendRegion(stream, hash, buffer, middle - SampleBlockSize / 2, SampleBlockSize);
            AppendRegion(stream, hash, buffer, length - SampleBlockSize, SampleBlockSize);
        }

        return hash.GetHashAndReset();
    }

    public static async Task<byte[]> ComputeSampledSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        var length = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SampleBlockSize];
        if (length <= SampleBlockSize)
        {
            await AppendAllAsync(stream, hash, buffer, cancellationToken);
        }
        else
        {
            var middle = length / 2;
            await AppendRegionAsync(stream, hash, buffer, 0, SampleBlockSize, cancellationToken);
            await AppendRegionAsync(stream, hash, buffer, middle - SampleBlockSize / 2, SampleBlockSize, cancellationToken);
            await AppendRegionAsync(stream, hash, buffer, length - SampleBlockSize, SampleBlockSize, cancellationToken);
        }

        return hash.GetHashAndReset();
    }

    private static FileStream OpenSequential(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, SampleBlockSize, FileOptions.SequentialScan);

    private static FileStream OpenAsync(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, SampleBlockSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void AppendAll(Stream stream, IncrementalHash hash, byte[] buffer)
    {
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    private static void AppendRegion(Stream stream, IncrementalHash hash, byte[] buffer, long start, long count)
    {
        stream.Position = start;
        long remaining = count;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static async Task AppendAllAsync(
        Stream stream, IncrementalHash hash, byte[] buffer, CancellationToken cancellationToken)
    {
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    private static async Task AppendRegionAsync(
        Stream stream, IncrementalHash hash, byte[] buffer, long start, long count, CancellationToken cancellationToken)
    {
        stream.Position = start;
        long remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }
}
