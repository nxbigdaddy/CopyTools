using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CopyTools.Infrastructure;

/// <summary>局域网直连文件传输帧协议：长度前缀 JSON 头 + 载荷，可选 gzip 压缩，SHA-256 校验完整性。</summary>
public static class LanFileTransfer
{
    public sealed record FileHeader(string RelativePath, long Length, string Sha256, bool Compressed);

    public static async Task SendAsync(
        Stream destination,
        string relativePath,
        Stream content,
        bool compress,
        CancellationToken cancellationToken = default)
    {
        var payload = compress
            ? await CompressToBytesAsync(content, cancellationToken)
            : await ReadAllBytesAsync(content, cancellationToken);
        var header = new FileHeader(relativePath, payload.Length, ComputeSha256Hex(payload), compress);
        var headerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header));
        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, headerBytes.Length);
        await destination.WriteAsync(lengthPrefix, cancellationToken);
        await destination.WriteAsync(headerBytes, cancellationToken);
        await destination.WriteAsync(payload, cancellationToken);
    }

    public static async Task<(FileHeader Header, byte[] Content)> ReceiveAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        var lengthPrefix = new byte[4];
        await ReadExactlyAsync(source, lengthPrefix, cancellationToken);
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (headerLength <= 0 || headerLength > 1024 * 1024)
        {
            throw new InvalidDataException("传输帧头长度无效。");
        }

        var headerBytes = new byte[headerLength];
        await ReadExactlyAsync(source, headerBytes, cancellationToken);
        var header = JsonSerializer.Deserialize<FileHeader>(Encoding.UTF8.GetString(headerBytes))
            ?? throw new InvalidDataException("传输帧头无效。");
        if (header.Length < 0 || header.Length > int.MaxValue)
        {
            throw new InvalidDataException("传输载荷长度无效。");
        }

        var content = new byte[header.Length];
        await ReadExactlyAsync(source, content, cancellationToken);
        if (!string.Equals(ComputeSha256Hex(content), header.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("传输内容 SHA-256 校验失败。");
        }

        return (header, content);
    }

    public static byte[] DecodeContent(FileHeader header, byte[] content)
    {
        if (!header.Compressed)
        {
            return content;
        }

        using var input = new MemoryStream(content);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static async Task<byte[]> CompressToBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            await content.CopyToAsync(gzip, cancellationToken);
        }

        return output.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await content.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    private static string ComputeSha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("传输流提前结束。");
            }

            total += read;
        }
    }
}
