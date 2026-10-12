using CopyTools.Domain;

namespace CopyTools.Engine;

internal static class SyncStateComparer
{
    private const double TimestampToleranceSeconds = 2;

    public static bool MatchesSource(FileEntry? current, SyncFileState? previous) =>
        Matches(
            current,
            previous?.SourceExists ?? false,
            previous?.SourceLength,
            previous?.SourceLastWriteTimeUtc,
            previous?.SourceContentHash);

    public static bool MatchesTarget(FileEntry? current, SyncFileState? previous) =>
        Matches(
            current,
            previous?.TargetExists ?? false,
            previous?.TargetLength,
            previous?.TargetLastWriteTimeUtc,
            previous?.TargetContentHash);

    public static bool FilesEqual(FileEntry source, FileEntry target) =>
        source.ContentHash is not null && target.ContentHash is not null
            ? string.Equals(source.ContentHash, target.ContentHash, StringComparison.OrdinalIgnoreCase)
            : source.Length == target.Length
        && Math.Abs((source.LastWriteTimeUtc - target.LastWriteTimeUtc).TotalSeconds)
            <= TimestampToleranceSeconds;

    private static bool Matches(
        FileEntry? current,
        bool previousExists,
        long? previousLength,
        DateTime? previousLastWriteTimeUtc,
        string? previousContentHash)
    {
        if (current is null)
        {
            return !previousExists;
        }

        if (previousContentHash is not null && current.ContentHash is not null)
            return previousExists && string.Equals(current.ContentHash, previousContentHash,
                StringComparison.OrdinalIgnoreCase);
        return previousExists
            && current.Length == previousLength
            && previousLastWriteTimeUtc is not null
            && Math.Abs((current.LastWriteTimeUtc - previousLastWriteTimeUtc.Value).TotalSeconds)
                <= TimestampToleranceSeconds;
    }
}
