namespace CopyTools.Domain;

public sealed record RunHistoryEntry(
    Guid Id,
    Guid? TaskId,
    string TaskName,
    CopyTaskRequest Request,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string Status,
    int SuccessfulFiles,
    int SkippedFiles,
    int FailedFiles,
    long CopiedBytes,
    IReadOnlyList<RunHistoryItem> Items,
    IReadOnlyList<QuarantineEntry>? QuarantineEntries = null)
{
    public IReadOnlyList<QuarantineEntry> EffectiveQuarantineEntries => QuarantineEntries ?? [];
}
