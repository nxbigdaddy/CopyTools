namespace CopyTools.Domain;

public enum CopyAction
{
    CopyNew,
    Overwrite,
    SkipExisting,
    SkipUnchanged,
    QuarantineTarget,
    QuarantineSource,
    RemoveSource,
    ConflictCopy,
    Verify,
    CreateDirectory,
    QuarantineDirectory,
    RenameCopy,
    ConflictNeedsDecision,
}
