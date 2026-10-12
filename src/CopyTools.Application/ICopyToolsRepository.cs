using CopyTools.Domain;

namespace CopyTools.Application;

public interface ICopyToolsRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCopyTask>> GetTasksAsync(CancellationToken cancellationToken = default);

    Task<SavedCopyTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveTaskAsync(SavedCopyTask task, CancellationToken cancellationToken = default);

    Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveRunAsync(RunHistoryEntry run, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunHistoryEntry>> GetRecentRunsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunHistoryEntry>> QueryRunsAsync(
        RunHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<RunHistoryEntry?> GetRunAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuarantineEntry>> GetQuarantineEntriesAsync(
        bool includeRestored = false,
        CancellationToken cancellationToken = default);

    Task MarkQuarantineRestoredAsync(
        Guid id,
        DateTime restoredAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskSchedule>> GetSchedulesAsync(CancellationToken cancellationToken = default);

    Task<TaskSchedule?> GetScheduleAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task SaveScheduleAsync(TaskSchedule schedule, CancellationToken cancellationToken = default);

    Task DeleteScheduleAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutoTriggerRule>> GetAutoTriggerRulesAsync(CancellationToken cancellationToken = default);

    Task SaveAutoTriggerRuleAsync(AutoTriggerRule rule, CancellationToken cancellationToken = default);

    Task DeleteAutoTriggerRuleAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StorageConnectionProfile>> GetStorageConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task SaveStorageConnectionAsync(
        StorageConnectionProfile connection,
        CancellationToken cancellationToken = default);

    Task DeleteStorageConnectionAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteRunsBeforeAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);

    Task<TaskExecutionOptions?> GetTaskExecutionOptionsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskExecutionOptions>> GetAllTaskExecutionOptionsAsync(
        CancellationToken cancellationToken = default);

    Task SaveTaskExecutionOptionsAsync(
        TaskExecutionOptions options,
        CancellationToken cancellationToken = default);

    Task<RunHistoryEntry?> GetLatestRunForTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IncompleteExecution>> GetIncompleteExecutionsAsync(
        CancellationToken cancellationToken = default);

    Task<IncompleteExecution?> GetIncompleteExecutionAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task SaveIncompleteExecutionAsync(
        IncompleteExecution execution,
        CancellationToken cancellationToken = default);

    Task SaveIncompleteExecutionItemAsync(
        Guid executionId,
        IncompleteExecutionItem item,
        CancellationToken cancellationToken = default);

    Task DeleteIncompleteExecutionAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationChannel>> GetNotificationChannelsAsync(
        CancellationToken cancellationToken = default);

    Task SaveNotificationChannelAsync(
        NotificationChannel channel,
        CancellationToken cancellationToken = default);

    Task DeleteNotificationChannelAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FilterRuleTemplate>> GetFilterRuleTemplatesAsync(CancellationToken cancellationToken = default);
    Task SaveFilterRuleTemplateAsync(FilterRuleTemplate template, CancellationToken cancellationToken = default);
    Task DeleteFilterRuleTemplateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskTemplate>> GetTaskTemplatesAsync(CancellationToken cancellationToken = default);
    Task SaveTaskTemplateAsync(TaskTemplate template, CancellationToken cancellationToken = default);
    Task DeleteTaskTemplateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes configuration records but retains run history, logs and quarantine entries.</summary>
    Task ClearConfigurationAsync(CancellationToken cancellationToken = default);
}
