using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class LocalStorageAdapter : IStorageAdapter
{
    private readonly string _root;

    public LocalStorageAdapter(string rootDirectory)
    {
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
    }

    public string DisplayName => _root.StartsWith("\\\\", StringComparison.Ordinal)
        ? $"Windows 网络共享：{_root}"
        : $"本地/移动存储：{_root}";

    public StorageCapabilities Capabilities => StorageCapabilities.All;

    public Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var directory = Resolve(relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<StorageItem>>([]);
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var items = new List<StorageItem>();
        foreach (var path in Directory.EnumerateDirectories(directory, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            items.Add(new StorageItem(ToRelative(path), true, 0, info.LastWriteTimeUtc));
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            items.Add(new StorageItem(ToRelative(path), false, info.Length, info.LastWriteTimeUtc));
        }

        return Task.FromResult<IReadOnlyList<StorageItem>>(items
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(
            Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));

    public async Task WriteAsync(
        string relativePath,
        Stream content,
        long? length = null,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = $"{target}.copytools-{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[1024 * 1024];
                long written = 0;
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    progress?.Report(read);
                }

                await output.FlushAsync(cancellationToken);
                if (length is not null && written != length.Value)
                {
                    throw new IOException("写入长度与声明长度不一致。");
                }
            }

            File.Move(temporary, target, overwrite);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    public Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }

        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Resolve(relativePath));
        return Task.CompletedTask;
    }

    public Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var source = Resolve(sourceRelativePath);
        var target = Resolve(targetRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(source))
        {
            File.Move(source, target, overwrite);
        }
        else
        {
            if (Directory.Exists(target) && overwrite)
            {
                Directory.Delete(target, true);
            }

            Directory.Move(source, target);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string Resolve(string relativePath)
    {
        var normalized = StoragePath.NormalizeRelative(relativePath).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, normalized));
        var relative = Path.GetRelativePath(_root, full);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("存储路径超出根目录。", nameof(relativePath));
        }

        return full;
    }

    private string ToRelative(string fullPath) =>
        Path.GetRelativePath(_root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
