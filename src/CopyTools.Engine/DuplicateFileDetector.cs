using System.Security.Cryptography;
using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class DuplicateFileDetector
{
    public async Task<IReadOnlyList<DuplicateFileGroup>> ScanAsync(
        IEnumerable<string> roots,
        FileFilterOptions? filters = null,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var scanner = new DirectoryScanner();
        var candidates = new List<FileEntry>();
        foreach (var root in roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"重复文件扫描目录不存在：{root}。");
            candidates.AddRange(await scanner.ScanAsync(root, filters, cancellationToken));
        }
        var repeatedSizes = candidates.GroupBy(file => file.Length).Where(group => group.Count() > 1)
            .SelectMany(group => group).ToArray();
        var total = repeatedSizes.Sum(file => file.Length);
        long completed = 0;
        var hashed = new List<(FileEntry File, string Hash)>();
        for (var index = 0; index < repeatedSizes.Length; index++)
        {
            var file = repeatedSizes[index];
            await using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            hashed.Add((file, hash)); completed += file.Length;
            progress?.Report(new CopyProgress(index + 1, repeatedSizes.Length, completed, total,
                file.FullPath, "正在计算重复文件哈希", file.Length, file.Length));
        }
        return hashed.GroupBy(item => (item.File.Length, item.Hash))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateFileGroup(group.Key.Hash, group.Key.Length,
                group.Select(item => new DuplicateFileEntry(item.File.FullPath, item.File.Length,
                    item.File.LastWriteTimeUtc)).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(group => group.ReclaimableBytes).ThenBy(group => group.Sha256).ToArray();
    }
}
