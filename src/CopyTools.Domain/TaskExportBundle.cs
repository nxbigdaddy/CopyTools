namespace CopyTools.Domain;

public sealed record TaskExportBundle(
    int FormatVersion,
    DateTime ExportedAtUtc,
    IReadOnlyList<SavedCopyTask> Tasks,
    IReadOnlyList<TaskSchedule> Schedules)
{
    public IReadOnlyList<AutoTriggerRule> AutoTriggerRules { get; init; } = [];
    public IReadOnlyList<TaskExecutionOptions> ExecutionOptions { get; init; } = [];
}
