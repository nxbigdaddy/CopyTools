namespace CopyTools.Domain;

public sealed record RunHistoryQuery(
    string? TaskName = null,
    string? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Limit = 200)
{
    public RunHistoryQuery Normalize() => this with
    {
        TaskName = string.IsNullOrWhiteSpace(TaskName) ? null : TaskName.Trim(),
        Status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim(),
        Limit = Math.Clamp(Limit, 1, 1000),
    };
}
