using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class SecureBackupArchiveService
{
    private static readonly byte[] Magic = "CTBAK001"u8.ToArray();
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;
    private const int ChunkSize = 4 * 1024 * 1024;
    private const int Pbkdf2Iterations = 250_000;

    public async Task<ArchiveBackupResult> CreateAsync(
        string sourceDirectory,
        string archivePath,
        bool compress = true,
        string? password = null,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourceDirectory);
        var output = Path.GetFullPath(archivePath);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("压缩备份源目录不存在。");
        }

        if (IsDescendant(output, source))
        {
            throw new CopyTaskValidationException("备份包不能创建在备份源目录内部。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var zipPath = string.IsNullOrEmpty(password)
            ? output
            : Path.Combine(Path.GetTempPath(), $"copytools-archive-{Guid.NewGuid():N}.zip");
        var outputTemporary = $"{output}.{Guid.NewGuid():N}.tmp";
        var files = Directory.EnumerateFiles(source, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        }).Select(path => new FileInfo(path)).ToArray();
        var totalBytes = files.Sum(file => file.Length);
        long completedBytes = 0;
        try
        {
            await CreateZipAsync(source, zipPath, files, totalBytes, compress, progress,
                value => completedBytes = value, cancellationToken);
            if (!string.IsNullOrEmpty(password))
            {
                await EncryptAsync(zipPath, outputTemporary, password, cancellationToken);
                File.Move(outputTemporary, output, true);
            }

            return new ArchiveBackupResult(
                output, files.Length, totalBytes, new FileInfo(output).Length,
                compress, !string.IsNullOrEmpty(password), DateTime.UtcNow);
        }
        catch
        {
            TryDelete(outputTemporary);
            if (string.IsNullOrEmpty(password))
            {
                TryDelete(output);
            }
            throw;
        }
        finally
        {
            if (!string.IsNullOrEmpty(password))
            {
                TryDelete(zipPath);
            }
        }
    }

    public async Task RestoreAsync(
        string archivePath,
        string restoreDirectory,
        string? password = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var archive = Path.GetFullPath(archivePath);
        var restoreRoot = Path.GetFullPath(restoreDirectory);
        if (!File.Exists(archive))
        {
            throw new FileNotFoundException("备份包不存在。", archive);
        }

        Directory.CreateDirectory(restoreRoot);
        var encrypted = await HasMagicAsync(archive, cancellationToken);
        if (encrypted && string.IsNullOrEmpty(password))
        {
            throw new CryptographicException("该备份包已加密，必须提供密码。");
        }

        var zipPath = encrypted
            ? Path.Combine(Path.GetTempPath(), $"copytools-restore-{Guid.NewGuid():N}.zip")
            : archive;
        try
        {
            if (encrypted)
            {
                await DecryptAsync(archive, zipPath, password!, cancellationToken);
            }

            await ExtractZipSafely(zipPath, restoreRoot, overwrite, cancellationToken);
        }
        finally
        {
            if (encrypted)
            {
                TryDelete(zipPath);
            }
        }
    }

    public async Task ChangePasswordAsync(
        string archivePath,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var archive = Path.GetFullPath(archivePath);
        if (!File.Exists(archive))
        {
            throw new FileNotFoundException("备份包不存在。", archive);
        }

        if (!await HasMagicAsync(archive, cancellationToken))
        {
            throw new InvalidDataException("该备份包未加密，无法更换密码。");
        }

        var zipPath = Path.Combine(Path.GetTempPath(), $"copytools-rekey-{Guid.NewGuid():N}.zip");
        var temporary = $"{archive}.{Guid.NewGuid():N}.tmp";
        try
        {
            await DecryptAsync(archive, zipPath, oldPassword, cancellationToken);
            await EncryptAsync(zipPath, temporary, newPassword, cancellationToken);
            File.Move(temporary, archive, true);
        }
        finally
        {
            TryDelete(zipPath);
            TryDelete(temporary);
        }
    }

    public static string GenerateRecoveryCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static async Task CreateZipAsync(
        string source,
        string zipPath,
        IReadOnlyList<FileInfo> files,
        long totalBytes,
        bool compress,
        IProgress<CopyProgress>? progress,
        Action<long> updateCompleted,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            zipPath, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        long completed = 0;
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var relative = Path.GetRelativePath(source, file.FullName).Replace('\\', '/');
            var entry = archive.CreateEntry(relative, compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            entry.ExternalAttributes = (int)file.Attributes;
            await using var input = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var entryStream = entry.Open();
            var buffer = new byte[1024 * 1024];
            long current = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await entryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                current += read;
                progress?.Report(new CopyProgress(
                    index, files.Count, completed + current, totalBytes,
                    relative, compress ? "正在压缩备份" : "正在打包备份", current, file.Length));
            }

            completed += file.Length;
            updateCompleted(completed);
        }

        archive.Dispose();
        await output.FlushAsync(cancellationToken);
    }

    private static async Task EncryptAsync(
        string inputPath,
        string outputPath,
        string password,
        CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations,
            HashAlgorithmName.SHA256, KeyLength);
        try
        {
            await using var input = File.OpenRead(inputPath);
            await using var output = new FileStream(
                outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await output.WriteAsync(Magic, cancellationToken);
            await output.WriteAsync(salt, cancellationToken);
            await WriteInt32Async(output, Pbkdf2Iterations, cancellationToken);
            await WriteInt32Async(output, ChunkSize, cancellationToken);
            await WriteInt64Async(output, input.Length, cancellationToken);
            var buffer = new byte[ChunkSize];
            var cipher = new byte[ChunkSize];
            using var aes = new AesGcm(key, TagLength);
            long index = 0;
            while (true)
            {
                var read = await ReadAtMostAsync(input, buffer, cancellationToken);
                if (read == 0) break;
                var nonce = RandomNumberGenerator.GetBytes(NonceLength);
                var tag = new byte[TagLength];
                var associated = BuildAssociatedData(index++, read, input.Length);
                aes.Encrypt(nonce, buffer.AsSpan(0, read), cipher.AsSpan(0, read), tag, associated);
                await WriteInt32Async(output, read, cancellationToken);
                await output.WriteAsync(nonce, cancellationToken);
                await output.WriteAsync(tag, cancellationToken);
                await output.WriteAsync(cipher.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task DecryptAsync(
        string inputPath,
        string outputPath,
        string password,
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(inputPath);
        var magic = new byte[Magic.Length];
        await ReadExactlyAsync(input, magic, cancellationToken);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("加密备份包标头无效。");
        var salt = new byte[SaltLength];
        await ReadExactlyAsync(input, salt, cancellationToken);
        var iterations = await ReadInt32Async(input, cancellationToken);
        var chunkSize = await ReadInt32Async(input, cancellationToken);
        var originalLength = await ReadInt64Async(input, cancellationToken);
        if (iterations is < 100_000 or > 2_000_000 || chunkSize is < 64 * 1024 or > 16 * 1024 * 1024 || originalLength < 0)
        {
            throw new InvalidDataException("加密备份包参数无效。");
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations,
            HashAlgorithmName.SHA256, KeyLength);
        try
        {
            await using var output = new FileStream(
                outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var cipher = new byte[chunkSize];
            var clear = new byte[chunkSize];
            using var aes = new AesGcm(key, TagLength);
            long index = 0;
            long written = 0;
            while (written < originalLength)
            {
                var length = await ReadInt32Async(input, cancellationToken);
                if (length <= 0 || length > chunkSize || length > originalLength - written)
                {
                    throw new InvalidDataException("加密备份包分块长度无效。");
                }

                var nonce = new byte[NonceLength];
                var tag = new byte[TagLength];
                await ReadExactlyAsync(input, nonce, cancellationToken);
                await ReadExactlyAsync(input, tag, cancellationToken);
                await ReadExactlyAsync(input, cipher.AsMemory(0, length), cancellationToken);
                var associated = BuildAssociatedData(index++, length, originalLength);
                aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, clear.AsSpan(0, length), associated);
                await output.WriteAsync(clear.AsMemory(0, length), cancellationToken);
                written += length;
            }

            if (input.Position != input.Length) throw new InvalidDataException("加密备份包包含未识别的尾部数据。");
            await output.FlushAsync(cancellationToken);
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task ExtractZipSafely(
        string zipPath,
        string restoreRoot,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(restoreRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.TrimEndingDirectorySeparator(restoreRoot) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("备份包包含越界路径，已拒绝恢复。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && !overwrite) throw new IOException($"恢复目标已存在：{entry.FullName}。");
            var temporary = $"{target}.copytools-restore-{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var input = entry.Open())
                await using (var output = new FileStream(
                    temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                File.Move(temporary, target, overwrite);
                File.SetLastWriteTimeUtc(target, entry.LastWriteTime.UtcDateTime);
                var attributes = (FileAttributes)entry.ExternalAttributes
                    & (FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System);
                File.SetAttributes(target, attributes);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
    }

    private static byte[] BuildAssociatedData(long index, int length, long originalLength)
    {
        var data = new byte[20];
        BinaryPrimitives.WriteInt64LittleEndian(data, index);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), length);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(12), originalLength);
        return data;
    }

    private static async Task<bool> HasMagicAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length < Magic.Length) return false;
        var header = new byte[Magic.Length];
        await ReadExactlyAsync(stream, header, cancellationToken);
        return header.AsSpan().SequenceEqual(Magic);
    }

    private static async Task<int> ReadAtMostAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0) throw new EndOfStreamException("加密备份包提前结束。");
            total += read;
        }
    }

    private static async Task WriteInt32Async(Stream stream, int value, CancellationToken cancellationToken)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        await stream.WriteAsync(buffer, cancellationToken);
    }

    private static async Task WriteInt64Async(Stream stream, long value, CancellationToken cancellationToken)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        await stream.WriteAsync(buffer, cancellationToken);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4];
        await ReadExactlyAsync(stream, buffer, cancellationToken);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static async Task<long> ReadInt64Async(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8];
        await ReadExactlyAsync(stream, buffer, cancellationToken);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    private static bool IsDescendant(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != "." && !Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
