namespace CopyTools.Domain;

public sealed record StorageItem(
    string RelativePath,
    bool IsDirectory,
    long Length,
    DateTime? LastWriteTimeUtc,
    string? ETag = null);
