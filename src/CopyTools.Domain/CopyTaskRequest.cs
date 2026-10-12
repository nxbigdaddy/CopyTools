namespace CopyTools.Domain;

public sealed record CopyTaskRequest(
    string SourceDirectory,
    string TargetDirectory,
    CopyMode Mode,
    FileFilterOptions? Filters = null,
    VerificationMode VerificationMode = VerificationMode.Quick,
    RetryPolicy? RetryPolicy = null,
    DeleteSafetyPolicy? DeleteSafetyPolicy = null,
    Guid? SynchronizationId = null,
    ExistingFilePolicy ExistingFilePolicy = ExistingFilePolicy.ModeDefault,
    PerformanceOptions? Performance = null,
    VersioningPolicy? Versioning = null,
    FileSecurityOptions? Security = null,
    string? VerificationProviderId = null,
    IReadOnlyList<string>? SourcePaths = null,
    ConflictResolutionPolicy ConflictResolutionPolicy = ConflictResolutionPolicy.KeepBoth,
    TransferMode TransferMode = TransferMode.Full)
{
    public IReadOnlyList<string> EffectiveSourcePaths => SourcePaths is { Count: > 0 }
        ? SourcePaths
        : string.IsNullOrWhiteSpace(SourceDirectory) ? [] : [SourceDirectory];
    public FileFilterOptions EffectiveFilters => Filters ?? FileFilterOptions.Empty;

    public RetryPolicy EffectiveRetryPolicy => RetryPolicy ?? RetryPolicy.Default;

    public DeleteSafetyPolicy EffectiveDeleteSafetyPolicy => DeleteSafetyPolicy ?? DeleteSafetyPolicy.Default;

    public PerformanceOptions EffectivePerformance => Performance ?? PerformanceOptions.Default;

    public VersioningPolicy EffectiveVersioning => Versioning ?? new VersioningPolicy();

    public FileSecurityOptions EffectiveSecurity => Security ?? new FileSecurityOptions();
}
