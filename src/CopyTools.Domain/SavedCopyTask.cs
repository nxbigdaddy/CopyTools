namespace CopyTools.Domain;

public sealed record SavedCopyTask(
    Guid Id,
    string Name,
    CopyTaskRequest Request,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
