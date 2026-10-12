using CopyTools.Domain;
using System.Collections.Concurrent;

namespace CopyTools.Application;

public sealed class ScheduledTaskRunner
{
    private readonly ICopyToolsRepository _repository;
    private readonly CopyWorkflow _workflow;
    private readonly ResumableExecutionService _resumableExecution;
    private readonly ITaskResultNotifier? _notifier;
    private readonly ISettingsStore? _settingsStore;
    private readonly RunConditionEvaluator _conditionEvaluator;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _taskGates = new();

    public ScheduledTaskRunner(
        ICopyToolsRepository repository,
        CopyWorkflow workflow,
        ITaskResultNotifier? notifier = null,
        ISettingsStore? settingsStore = null,
        RunConditionEvaluator? conditionEvaluator = null)
    {
        _repository = repository;
        _workflow = workflow;
        _resumableExecution = new ResumableExecutionService(repository, workflow);
        _notifier = notifier;
        _settingsStore = settingsStore;
        _conditionEvaluator = conditionEvaluator ?? new RunConditionEvaluator();
    }

    public async Task<ScheduledRunResult> RunAsync(
        TaskSchedule schedule,
        CancellationToken cancellationToken = default,
        CopyTaskRequest? requestOverride = null)
    {
        var executionGate = _taskGates.GetOrAdd(schedule.TaskId, _ => new SemaphoreSlim(1, 1));
        await executionGate.WaitAsync(cancellationToken);
        try
        {
            var task = await _repository.GetTaskAsync(schedule.TaskId, cancellationToken);
            if (task is null)
            {
                return new ScheduledRunResult(
                    schedule.TaskId,
                    "已删除任务",
                    "失败",
                    false,
                    DateTime.UtcNow,
                    Message: "计划关联的任务不存在。");
            }

            if (requestOverride is not null)
            {
                task = task with { Request = requestOverride };
            }

            var conditionEvaluation = await _conditionEvaluator.EvaluateAsync(
                schedule.EffectiveConditions, task, _repository, cancellationToken);
            if (!conditionEvaluation.Passed)
            {
                return new ScheduledRunResult(
                    task.Id,
                    task.Name,
                    "已跳过",
                    false,
                    DateTime.UtcNow,
                    Message: conditionEvaluation.Reason);
            }

            if (schedule.TimeWindow is not null && !schedule.TimeWindow.Contains(DateTime.Now.TimeOfDay))
            {
                return new ScheduledRunResult(
                    task.Id,
                    task.Name,
                    "已跳过",
                    false,
                    DateTime.UtcNow,
                    Message: "当前不在允许执行的时间窗口内。");
            }

            var expandedRequest = PathTemplateExpander.Expand(task.Request, task.Name);
            var request = expandedRequest.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
                ? expandedRequest with { SynchronizationId = task.Id }
                : expandedRequest;
            if (_settingsStore is null)
            {
                request = request with
                {
                    Performance = request.EffectivePerformance with { BackgroundLowImpact = true },
                };
            }
            else
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);
                var configured = request.EffectivePerformance.Normalize();
                var backgroundLimit = settings.BackgroundSpeedLimitMbPerSecond == 0
                    ? (long?)null : settings.BackgroundSpeedLimitMbPerSecond * 1024L * 1024L;
                var effectiveLimit = configured.SpeedLimitBytesPerSecond is null ? backgroundLimit
                    : backgroundLimit is null ? configured.SpeedLimitBytesPerSecond
                    : Math.Min(configured.SpeedLimitBytesPerSecond.Value, backgroundLimit.Value);
                request = request with
                {
                    Performance = configured with
                    {
                        MaxParallelFiles = Math.Min(configured.MaxParallelFiles, settings.BackgroundMaxParallelFiles),
                        SpeedLimitBytesPerSecond = effectiveLimit,
                        AutoTune = false,
                        BackgroundLowImpact = false,
                    },
                };
            }
            var normalizedTask = task with { Request = request };
            CopyPlan plan;
            try
            {
                plan = await _workflow.CreatePlanAsync(request, cancellationToken);
            }
            catch (Exception exception) when (
                exception is CopyTaskValidationException or IOException or UnauthorizedAccessException)
            {
                var notificationError = await NotifyStatusAsync(
                    task, "失败", exception.Message, DateTime.UtcNow, cancellationToken);
                return new ScheduledRunResult(
                    task.Id,
                    task.Name,
                    "失败",
                    false,
                    DateTime.UtcNow,
                    Message: CombineMessages(exception.Message, notificationError));
            }

            var blockReason = GetBlockReason(plan, schedule);
            if (blockReason is not null)
            {
                await _repository.SaveRunAsync(
                    RunHistoryFactory.Blocked(normalizedTask, plan, blockReason, DateTime.UtcNow),
                    cancellationToken);
                var notificationError = await NotifyStatusAsync(
                    task, "等待人工确认", blockReason, DateTime.UtcNow, cancellationToken);
                return new ScheduledRunResult(
                    task.Id,
                    task.Name,
                    "等待人工确认",
                    false,
                    DateTime.UtcNow,
                    Message: CombineMessages(blockReason, notificationError));
            }

            var (_, execution) = await _resumableExecution.ExecuteAsync(
                normalizedTask, plan, cancellationToken: cancellationToken);
            await _repository.SaveRunAsync(
                RunHistoryFactory.FromExecution(normalizedTask, plan, execution),
                cancellationToken);
            var notificationErrors = await NotifyAsync(task, execution, cancellationToken);
            return new ScheduledRunResult(
                task.Id,
                task.Name,
                execution.Status,
                true,
                execution.CompletedAtUtc,
                execution,
                CombineMessages(
                    execution.IsSuccessful ? null : "部分文件执行失败，请查看运行历史。",
                    notificationErrors));
        }
        finally
        {
            executionGate.Release();
        }
    }

    private async Task<string?> NotifyAsync(
        SavedCopyTask task,
        CopyExecutionResult execution,
        CancellationToken cancellationToken)
    {
        if (_notifier is null) return null;
        var errors = await _notifier.SendAsync(new TaskResultNotification(
            task.Id, task.Name, execution.Status, execution.CompletedAtUtc,
            execution.SuccessfulFiles, execution.Failures.Count, execution.CopiedBytes), cancellationToken);
        return errors.Count == 0 ? null : $"通知失败：{string.Join("；", errors)}";
    }

    private async Task<string?> NotifyStatusAsync(
        SavedCopyTask task, string status, string message, DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_notifier is null) return null;
        var errors = await _notifier.SendAsync(new TaskResultNotification(
            task.Id, task.Name, status, completedAtUtc, 0, status == "失败" ? 1 : 0, 0, message),
            cancellationToken);
        return errors.Count == 0 ? null : $"通知失败：{string.Join("；", errors)}";
    }

    private static string? CombineMessages(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? second
        : string.IsNullOrWhiteSpace(second) ? first
        : $"{first} {second}";

    private static string? GetBlockReason(CopyPlan plan, TaskSchedule schedule)
    {
        if (plan.UnresolvedConflicts > 0)
            return $"自动任务发现 {plan.UnresolvedConflicts:N0} 个需要逐项确认的双向冲突。";
        if (plan.FilesToOverwrite > 0 && !schedule.AllowOverwrite)
        {
            return $"自动任务需要覆盖 {plan.FilesToOverwrite:N0} 个文件，但未授权自动覆盖。";
        }

        if ((plan.FilesToQuarantine > 0 || plan.SourceFilesToRemove > 0)
            && !schedule.AllowQuarantine)
        {
            return $"自动任务需要隔离或清理 {plan.FilesToQuarantine + plan.SourceFilesToRemove:N0} 个文件，"
                + "但未授权自动隔离。";
        }

        return null;
    }
}
