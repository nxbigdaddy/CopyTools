namespace CopyTools.Domain;

public sealed record FilterRuleTemplate(
    Guid Id,
    string Name,
    FileFilterOptions Filters,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record TaskTemplate(
    Guid Id,
    string Name,
    CopyMode Mode,
    FileFilterOptions Filters,
    VerificationMode VerificationMode,
    RetryPolicy RetryPolicy,
    DeleteSafetyPolicy DeleteSafetyPolicy,
    ExistingFilePolicy ExistingFilePolicy,
    PerformanceOptions Performance,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
