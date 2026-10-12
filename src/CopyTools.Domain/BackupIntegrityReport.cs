namespace CopyTools.Domain;

public sealed record BackupIntegrityReport(
    int CheckedObjects,
    int MissingObjects,
    int CorruptedObjects,
    IReadOnlyList<BackupIntegrityIssue> Issues)
{
    public bool IsHealthy => MissingObjects == 0 && CorruptedObjects == 0;
}
