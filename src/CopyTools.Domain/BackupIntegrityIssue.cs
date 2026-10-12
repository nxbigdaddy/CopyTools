namespace CopyTools.Domain;

public enum BackupIntegrityIssueKind
{
    Missing,
    Corrupted,
}

public sealed record BackupIntegrityIssue(
    Guid SnapshotId,
    string RelativePath,
    string ObjectRelativePath,
    BackupIntegrityIssueKind Kind,
    string ExpectedSha256);
