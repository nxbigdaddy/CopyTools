using CopyTools.Domain;

namespace CopyTools.Engine;

/// <summary>比较两个目录树，输出并排差异行，用于同步前可视化。</summary>
public sealed class DirectoryDiffService
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);
    private readonly DirectoryScanner _scanner;

    public DirectoryDiffService(DirectoryScanner? scanner = null)
    {
        _scanner = scanner ?? new DirectoryScanner();
    }

    public async Task<IReadOnlyList<DirectoryDiffRow>> CompareAsync(
        string leftDirectory,
        string rightDirectory,
        FileFilterOptions? filters = null,
        CancellationToken cancellationToken = default)
    {
        var leftFiles = await _scanner.ScanAsync(leftDirectory, filters, cancellationToken);
        var rightFiles = await _scanner.ScanAsync(rightDirectory, filters, cancellationToken);
        var left = leftFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var right = rightFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        return left.Keys
            .Concat(right.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                left.TryGetValue(path, out var leftFile);
                right.TryGetValue(path, out var rightFile);
                var side = (leftFile, rightFile) switch
                {
                    (null, _) => DiffSide.RightOnly,
                    (_, null) => DiffSide.LeftOnly,
                    _ when SameFile(leftFile!, rightFile!) => DiffSide.Identical,
                    _ => DiffSide.BothChanged,
                };
                return new DirectoryDiffRow(
                    path,
                    leftFile is not null,
                    rightFile is not null,
                    leftFile?.Length,
                    rightFile?.Length,
                    side);
            })
            .ToArray();
    }

    private static bool SameFile(FileEntry left, FileEntry right) =>
        left.Length == right.Length
        && Math.Abs((left.LastWriteTimeUtc - right.LastWriteTimeUtc).TotalSeconds) <= TimestampTolerance.TotalSeconds;
}
