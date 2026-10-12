namespace CopyTools.Domain;

public sealed record RestoreDrillResult(
    Guid SnapshotId,
    int VerifiedFiles,
    IReadOnlyList<string> FailedRelativePaths)
{
    public bool IsSuccessful => FailedRelativePaths.Count == 0;
}
