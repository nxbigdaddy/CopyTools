namespace CopyTools.Domain;

public sealed record ScheduledRunResult(
    Guid TaskId,
    string TaskName,
    string Status,
    bool Executed,
    DateTime CompletedAtUtc,
    CopyExecutionResult? ExecutionResult = null,
    string? Message = null);
