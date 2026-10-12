using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Application;

public sealed class StorageTransferService
{
    public async Task<CopyExecutionResult> CopyTreeAsync(
        IStorageAdapter source,
        IStorageAdapter target,
        string sourceDirectory = "",
        string targetDirectory = "",
        bool overwrite = true,
        RetryPolicy? retryPolicy = null,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var policy = (retryPolicy ?? RetryPolicy.Default).Normalize();
        var started = DateTime.UtcNow;
        var items = await source.ListAsync(sourceDirectory, recursive: true, cancellationToken);
        var directories = items.Where(item => item.IsDirectory)
            .OrderBy(item => item.RelativePath.Count(character => character == '/'))
            .ToArray();
        var files = items.Where(item => !item.IsDirectory).ToArray();
        foreach (var directory in directories)
        {
            var relative = RelativeTo(directory.RelativePath, sourceDirectory);
            await target.CreateDirectoryAsync(StoragePath.Combine(targetDirectory, relative), cancellationToken);
        }

        var failures = new List<CopyItemFailure>();
        var results = new List<CopyItemExecutionResult>();
        long copied = 0;
        var completed = 0;
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            var relative = RelativeTo(file.RelativePath, sourceDirectory);
            var targetPath = StoragePath.Combine(targetDirectory, relative);
            var attempts = 0;
            while (true)
            {
                attempts++;
                try
                {
                    await using var stream = await source.OpenReadAsync(file.RelativePath, cancellationToken);
                    long current = 0;
                    var byteProgress = new InlineProgress<long>(increment =>
                    {
                        current += increment;
                        progress?.Report(new CopyProgress(
                            completed, files.Length, copied + current, total,
                            relative, "正在传输存储对象", current, file.Length));
                    });
                    await target.WriteAsync(
                        targetPath, stream, file.Length, overwrite, byteProgress, cancellationToken);
                    copied += file.Length;
                    results.Add(new CopyItemExecutionResult(
                        relative, overwrite ? CopyAction.Overwrite : CopyAction.CopyNew,
                        "成功", file.Length, null, attempts));
                    break;
                }
                catch (Exception exception) when (
                    exception is IOException or HttpRequestException or UnauthorizedAccessException
                    && attempts < policy.MaxAttempts)
                {
                    await Task.Delay(policy.Delay, cancellationToken);
                }
                catch (Exception exception) when (
                    exception is IOException or HttpRequestException or UnauthorizedAccessException)
                {
                    failures.Add(new CopyItemFailure(relative, exception.Message, attempts));
                    results.Add(new CopyItemExecutionResult(
                        relative, overwrite ? CopyAction.Overwrite : CopyAction.CopyNew,
                        "失败", 0, exception.Message, attempts));
                    break;
                }
            }

            completed++;
        }

        return new CopyExecutionResult(
            results.Count(item => item.Result == "成功"),
            0,
            copied,
            failures,
            false,
            started,
            DateTime.UtcNow,
            results);
    }

    private static string RelativeTo(string path, string root)
    {
        var normalizedPath = StoragePath.NormalizeRelative(path);
        var normalizedRoot = StoragePath.NormalizeRelative(root);
        if (string.IsNullOrEmpty(normalizedRoot))
        {
            return normalizedPath;
        }

        return normalizedPath.StartsWith($"{normalizedRoot}/", StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[(normalizedRoot.Length + 1)..]
            : normalizedPath;
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
