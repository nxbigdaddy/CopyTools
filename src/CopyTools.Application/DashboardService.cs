using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record DashboardSummary(
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    int BlockedRuns,
    double SuccessRate,
    long TotalCopiedBytes,
    int ActiveTasks,
    int ScheduledTasks,
    int QuarantinedFiles,
    int IncompleteExecutions,
    DateTime? LastRunAtUtc);

/// <summary>聚合运行历史与任务状态，生成仪表盘首页概览。</summary>
public sealed class DashboardService
{
    private readonly ICopyToolsRepository _repository;

    public DashboardService(ICopyToolsRepository repository)
    {
        _repository = repository;
    }

    public async Task<DashboardSummary> BuildAsync(CancellationToken cancellationToken = default)
    {
        var runs = await _repository.GetRecentRunsAsync(1000, cancellationToken);
        var tasks = await _repository.GetTasksAsync(cancellationToken);
        var schedules = await _repository.GetSchedulesAsync(cancellationToken);
        var quarantine = await _repository.GetQuarantineEntriesAsync(includeRestored: false, cancellationToken);
        var incomplete = await _repository.GetIncompleteExecutionsAsync(cancellationToken);
        var successful = runs.Count(run => run.Status is "成功" or "部分成功");
        var failed = runs.Count(run => run.Status is "失败" or "已取消");
        var blocked = runs.Count(run => run.Status == "等待人工确认");
        return new DashboardSummary(
            runs.Count,
            successful,
            failed,
            blocked,
            runs.Count == 0 ? 0 : successful * 100.0 / runs.Count,
            runs.Sum(run => run.CopiedBytes),
            tasks.Count,
            schedules.Count(schedule => schedule.Enabled),
            quarantine.Count,
            incomplete.Count,
            runs.Count == 0 ? null : runs.Max(run => run.CompletedAtUtc));
    }
}
