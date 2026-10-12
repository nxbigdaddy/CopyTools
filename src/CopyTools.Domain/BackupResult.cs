namespace CopyTools.Domain;

public sealed record BackupResult(
    BackupSnapshot Snapshot,
    int NewObjects,
    int ReusedObjects,
    long WrittenBytes);
