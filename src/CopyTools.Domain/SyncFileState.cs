namespace CopyTools.Domain;

public sealed record SyncFileState(
    string RelativePath,
    bool SourceExists,
    long? SourceLength,
    DateTime? SourceLastWriteTimeUtc,
    bool TargetExists,
    long? TargetLength,
    DateTime? TargetLastWriteTimeUtc,
    string? SourceContentHash = null,
    string? TargetContentHash = null);
