using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class DirectoryScanner
{
    private readonly FileFilterMatcher _filterMatcher;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
    };

    public DirectoryScanner(FileFilterMatcher? filterMatcher = null)
    {
        _filterMatcher = filterMatcher ?? new FileFilterMatcher();
    }

    public async Task<IReadOnlyList<FileEntry>> ScanAsync(
        string rootDirectory,
        FileFilterOptions? filters = null,
        CancellationToken cancellationToken = default,
        ContentHashMode hashMode = ContentHashMode.None)
    {
        var effectiveFilters = filters ?? FileFilterOptions.Empty;
        return await Task.Run<IReadOnlyList<FileEntry>>(
            () => Scan(rootDirectory, effectiveFilters, hashMode, cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<EmptyDirectoryEntry>> ScanEmptyDirectoriesAsync(
        string rootDirectory,
        FileFilterOptions? filters = null,
        CancellationToken cancellationToken = default) =>
        await Task.Run<IReadOnlyList<EmptyDirectoryEntry>>(
            () => ScanEmptyDirectories(rootDirectory, filters ?? FileFilterOptions.Empty, cancellationToken),
            cancellationToken);

    private IReadOnlyList<FileEntry> Scan(
        string rootDirectory,
        FileFilterOptions filters,
        ContentHashMode hashMode,
        CancellationToken cancellationToken)
    {
        var files = new List<FileEntry>();

        foreach (var path in EnumerateFiles(rootDirectory, filters, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var metadata = info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                ? info.ResolveLinkTarget(returnFinalTarget: true) as FileInfo
                    ?? throw new IOException($"文件符号链接目标不存在：{path}")
                : info;
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootDirectory, path));
            if (IsCopyToolsTemporaryFile(info.Name))
            {
                continue;
            }
            if (!_filterMatcher.IsMatch(metadata, relativePath, filters))
            {
                continue;
            }

            files.Add(new FileEntry(
                relativePath,
                path,
                metadata.Length,
                metadata.LastWriteTimeUtc)
            {
                Attributes = metadata.Attributes,
            });
        }

        files.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));

        if (hashMode != ContentHashMode.None)
        {
            var hashes = new string?[files.Count];
            try
            {
                Parallel.For(0, files.Count, index =>
                {
                    hashes[index] = ComputeContentHash(files[index].FullPath, hashMode, cancellationToken);
                });
            }
            catch (AggregateException exception)
            {
                var cancelled = exception.InnerExceptions.FirstOrDefault(item => item is OperationCanceledException);
                if (cancelled is not null)
                {
                    throw cancelled;
                }

                throw;
            }

            files = files.Select((file, index) => file with { ContentHash = hashes[index] }).ToList();
        }

        return files;
    }

    private static string? ComputeContentHash(
        string path,
        ContentHashMode hashMode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hash = hashMode switch
        {
            ContentHashMode.Sha256 => ContentHasher.ComputeSha256Hex(path),
            ContentHashMode.SampledSha256 => ContentHasher.ComputeSampledSha256Hex(path),
            _ => null,
        };
        cancellationToken.ThrowIfCancellationRequested();
        return hash;
    }

    private static IEnumerable<string> EnumerateFiles(
        string rootDirectory,
        FileFilterOptions filters,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var visitedLinkTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar),
        };
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var entries = Directory.EnumerateFileSystemEntries(directory, "*", EnumerationOptions)
                .Select(path => (Path: path, Attributes: File.GetAttributes(path)))
                .OrderBy(entry => entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .ToArray();
            foreach (var entry in entries)
            {
                var path = entry.Path;
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = entry.Attributes;
                var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (isReparsePoint && filters.ReparsePointPolicy != ReparsePointPolicy.FollowAll) continue;
                    if (!filters.IncludeHiddenFiles && attributes.HasFlag(FileAttributes.Hidden)) continue;
                    if (!filters.IncludeSystemFiles && attributes.HasFlag(FileAttributes.System)) continue;
                    if (isReparsePoint)
                    {
                        var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
                        if (target is null) continue;
                        var canonicalTarget = Path.GetFullPath(target.FullName).TrimEnd(Path.DirectorySeparatorChar);
                        if (!visitedLinkTargets.Add(canonicalTarget)) continue;
                    }
                    else if (!visitedLinkTargets.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)))
                    {
                        continue;
                    }
                    pending.Push(path);
                    continue;
                }
                if (isReparsePoint && filters.ReparsePointPolicy == ReparsePointPolicy.Skip) continue;
                yield return path;
            }
        }
    }

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool IsCopyToolsTemporaryFile(string name) =>
        name.StartsWith(".", StringComparison.Ordinal)
        && (name.EndsWith(".copytools-partial", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".copytools-partial.json", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".copytools-partial.json.", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<EmptyDirectoryEntry> ScanEmptyDirectories(
        string rootDirectory,
        FileFilterOptions filters,
        CancellationToken cancellationToken)
    {
        var result = new List<EmptyDirectoryEntry>();
        foreach (var path in EnumerateDirectories(rootDirectory, filters, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(path);
            if ((!filters.IncludeHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden))
                || (!filters.IncludeSystemFiles && info.Attributes.HasFlag(FileAttributes.System)))
            {
                continue;
            }

            using var contents = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            if (!contents.MoveNext())
            {
                result.Add(new EmptyDirectoryEntry(
                    NormalizeRelativePath(Path.GetRelativePath(rootDirectory, path)),
                    path));
            }
        }

        result.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        return result;
    }

    private static IEnumerable<string> EnumerateDirectories(
        string rootDirectory,
        FileFilterOptions filters,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var visitedLinkTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar),
        };
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var entries = Directory.EnumerateDirectories(directory, "*", EnumerationOptions)
                .Select(path => new DirectoryInfo(path))
                .OrderBy(info => info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .ToArray();
            foreach (var info in entries)
            {
                var path = info.FullName;
                var isLink = info.Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (isLink && filters.ReparsePointPolicy != ReparsePointPolicy.FollowAll) continue;
                if (!filters.IncludeHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                if (!filters.IncludeSystemFiles && info.Attributes.HasFlag(FileAttributes.System)) continue;
                if (isLink)
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is null) continue;
                    var canonicalTarget = Path.GetFullPath(target.FullName).TrimEnd(Path.DirectorySeparatorChar);
                    if (!visitedLinkTargets.Add(canonicalTarget)) continue;
                }
                else if (!visitedLinkTargets.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)))
                {
                    continue;
                }
                yield return path;
                pending.Push(path);
            }
        }
    }
}
