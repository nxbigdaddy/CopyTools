namespace CopyTools.Domain;

public sealed record CopyPlanItem(
    string RelativePath,
    string SourcePath,
    string TargetPath,
    long Length,
    DateTime LastWriteTimeUtc,
    CopyAction Action,
    string Reason)
{
    public FileAttributes SourceAttributes { get; init; } = FileAttributes.Normal;

    public bool IsDirectory { get; init; }

    public ConflictOrigin ConflictOrigin { get; init; }

    public bool WillCopy => Action is CopyAction.CopyNew
        or CopyAction.Overwrite
        or CopyAction.ConflictCopy
        or CopyAction.RenameCopy;

    public bool WillExecute => Action is not CopyAction.SkipExisting
        and not CopyAction.SkipUnchanged
        and not CopyAction.ConflictNeedsDecision;
}
