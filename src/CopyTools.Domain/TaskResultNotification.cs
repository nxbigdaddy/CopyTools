namespace CopyTools.Domain;

public sealed record TaskResultNotification(
    Guid TaskId,
    string TaskName,
    string Status,
    DateTime CompletedAtUtc,
    int SuccessfulFiles,
    int FailedFiles,
    long CopiedBytes,
    string? Message = null);
