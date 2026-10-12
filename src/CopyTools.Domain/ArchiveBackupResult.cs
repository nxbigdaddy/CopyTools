namespace CopyTools.Domain;

public sealed record ArchiveBackupResult(
    string ArchivePath,
    int FileCount,
    long SourceBytes,
    long ArchiveBytes,
    bool Compressed,
    bool Encrypted,
    DateTime CompletedAtUtc);
