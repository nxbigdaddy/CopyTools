namespace CopyTools.Domain;

public sealed record IncompleteExecutionItem(
    string RelativePath,
    CopyAction Action,
    long Length,
    DateTime SourceLastWriteTimeUtc,
    DateTime CompletedAtUtc);

public sealed record IncompleteExecution(
    Guid Id,
    Guid TaskId,
    CopyTaskRequest Request,
    DateTime PlanCreatedAtUtc,
    DateTime StartedAtUtc,
    IReadOnlyList<IncompleteExecutionItem> CompletedItems);
