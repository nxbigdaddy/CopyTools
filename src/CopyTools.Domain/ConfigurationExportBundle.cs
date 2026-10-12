namespace CopyTools.Domain;

/// <summary>
/// A portable, non-secret configuration snapshot. Credential target names are
/// included so a restore on the same Windows account can reuse Credential
/// Manager entries, but credential values are never serialized.
/// </summary>
public sealed record ConfigurationExportBundle(
    int FormatVersion,
    DateTime ExportedAtUtc,
    ApplicationSettings Settings,
    IReadOnlyList<SavedCopyTask> Tasks,
    IReadOnlyList<TaskSchedule> Schedules,
    IReadOnlyList<AutoTriggerRule> AutoTriggerRules,
    IReadOnlyList<TaskExecutionOptions> ExecutionOptions,
    IReadOnlyList<StorageConnectionProfile> StorageConnections,
    IReadOnlyList<NotificationChannel> NotificationChannels,
    IReadOnlyList<FilterRuleTemplate> FilterRuleTemplates,
    IReadOnlyList<TaskTemplate> TaskTemplates);

public sealed record ConfigurationImportResult(
    int Tasks,
    int StorageConnections,
    int NotificationChannels,
    int Templates,
    bool ReplacedExisting);
