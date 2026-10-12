using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Application;

public sealed class CopyWorkflow
{
    private readonly CopyPlanService _planService;
    private readonly CopyExecutor _executor;

    public CopyWorkflow(
        CopyPlanService? planService = null,
        CopyExecutor? executor = null)
    {
        _planService = planService ?? new CopyPlanService();
        _executor = executor ?? new CopyExecutor();
    }

    public Task<CopyPlan> CreatePlanAsync(
        CopyTaskRequest request,
        CancellationToken cancellationToken = default) =>
        _planService.CreatePlanAsync(request, cancellationToken);

    public async Task<CopyExecutionResult> ExecuteAsync(
        CopyPlan plan,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        CopyExecutionControl? executionControl = null,
        Func<CopyPlanItem, CopyItemExecutionResult, CancellationToken, Task>? itemCompleted = null)
    {
        if (plan.UnresolvedConflicts > 0)
            throw new CopyTaskValidationException(
                $"计划中仍有 {plan.UnresolvedConflicts} 个未确认的双向冲突，已阻止执行。");
        var result = await _executor.ExecuteAsync(plan, progress, cancellationToken, executionControl, itemCompleted);
        if (result.IsSuccessful)
        {
            await _planService.SaveSuccessfulSnapshotAsync(plan.Request, cancellationToken);
        }

        return result;
    }

    public Task<CopyExecutionResult> RetryFailuresAsync(
        CopyPlan originalPlan,
        CopyExecutionResult previousResult,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        CopyExecutionControl? executionControl = null)
    {
        var failedKeys = previousResult.EffectiveItems
            .Where(item => item.Result == "失败")
            .Select(item => (item.RelativePath, item.Action))
            .ToHashSet();
        var failedItems = originalPlan.Items
            .Where(item => failedKeys.Contains((item.RelativePath, item.Action)))
            .ToArray();
        if (failedItems.Length == 0)
        {
            throw new InvalidOperationException("上次执行没有可重试的失败项。");
        }

        return ExecuteAsync(
            originalPlan with { Items = failedItems, CreatedAtUtc = DateTime.UtcNow },
            progress,
            cancellationToken,
            executionControl);
    }
}
