namespace CopyTools.Domain;

public sealed record CopyPlan(
    CopyTaskRequest Request,
    IReadOnlyList<CopyPlanItem> Items,
    DateTime CreatedAtUtc)
{
    public int TotalFiles => Items.Count;

    public int FilesToCopy => Items.Count(item => item.WillCopy);

    public int NewFiles => Items.Count(item => item.Action == CopyAction.CopyNew);

    public int FilesToOverwrite => Items.Count(item => item.Action == CopyAction.Overwrite);

    public int FilesToRename => Items.Count(item => item.Action == CopyAction.RenameCopy);

    public int SkippedFiles => Items.Count(item => item.Action is CopyAction.SkipExisting or CopyAction.SkipUnchanged);

    public int FilesToQuarantine => Items.Count(item =>
        item.Action is CopyAction.QuarantineTarget or CopyAction.QuarantineSource);

    public int TargetFilesToQuarantine => Items.Count(item => item.Action == CopyAction.QuarantineTarget);

    public int SourceFilesToQuarantine => Items.Count(item => item.Action == CopyAction.QuarantineSource);

    public int SourceFilesToRemove => Items.Count(item => item.Action == CopyAction.RemoveSource);

    public int Conflicts => Items.Where(item => item.ConflictOrigin != ConflictOrigin.None)
        .Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public int UnresolvedConflicts => Items.Count(item => item.Action == CopyAction.ConflictNeedsDecision) / 2;

    public int FilesToVerify => Items.Count(item => item.Action == CopyAction.Verify);

    public int DirectoriesToCreate => Items.Count(item => item.Action == CopyAction.CreateDirectory);

    public int DirectoriesToQuarantine => Items.Count(item => item.Action == CopyAction.QuarantineDirectory);

    public int OperationsToExecute => Items.Count(item => item.WillExecute);

    public long BytesToCopy => Items.Where(item => item.WillCopy).Sum(item => item.Length);
}
