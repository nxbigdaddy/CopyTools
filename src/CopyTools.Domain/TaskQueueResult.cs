namespace CopyTools.Domain;

public sealed record TaskQueueItemResult(
    Guid TaskId,
    string TaskName,
    string Status,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string? Message = null);

public sealed record TaskQueueResult(
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<TaskQueueItemResult> Items)
{
    public bool IsSuccessful => Items.All(item => item.Status == "成功");
}
