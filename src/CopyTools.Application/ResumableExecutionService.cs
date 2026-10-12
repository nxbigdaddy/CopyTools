using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Application;

public sealed class ResumableExecutionService(
    ICopyToolsRepository repository,
    CopyWorkflow workflow)
{
    public Task<IReadOnlyList<IncompleteExecution>> GetIncompleteAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetIncompleteExecutionsAsync(cancellationToken);

    public async Task<(CopyPlan Plan, CopyExecutionResult Result)> ExecuteAsync(
        SavedCopyTask task,
        CopyPlan plan,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        CopyExecutionControl? executionControl = null)
    {
        var previous = await repository.GetIncompleteExecutionAsync(task.Id, cancellationToken);
        var resumableItems = previous?.CompletedItems
            .Where(item => IsStillCompleted(plan, item))
            .ToArray() ?? [];
        var completedKeys = resumableItems
            .Select(item => (item.RelativePath, item.Length, item.SourceLastWriteTimeUtc))
            .ToHashSet();
        var resumedPlan = plan with
        {
            Items = plan.Items.Where(item =>
                    !item.WillExecute || !completedKeys.Contains((item.RelativePath, item.Length, item.LastWriteTimeUtc)))
                .ToArray(),
        };

        var execution = new IncompleteExecution(
            Guid.NewGuid(), task.Id, resumedPlan.Request, resumedPlan.CreatedAtUtc,
            DateTime.UtcNow, resumableItems);
        await repository.SaveIncompleteExecutionAsync(execution, cancellationToken);
        foreach (var item in resumableItems)
        {
            await repository.SaveIncompleteExecutionItemAsync(execution.Id, item, cancellationToken);
        }

        var result = await workflow.ExecuteAsync(
            resumedPlan,
            progress,
            cancellationToken,
            executionControl,
            async (item, itemResult, _) =>
            {
                if (itemResult.Result == "成功")
                {
                    await repository.SaveIncompleteExecutionItemAsync(
                        execution.Id,
                        new IncompleteExecutionItem(
                            item.RelativePath, item.Action, item.Length,
                            item.LastWriteTimeUtc, DateTime.UtcNow),
                        CancellationToken.None);
                }
            });

        if (result.IsSuccessful)
        {
            await repository.DeleteIncompleteExecutionAsync(execution.Id, CancellationToken.None);
        }

        return (resumedPlan, result);
    }

    public async Task<(CopyPlan Plan, CopyExecutionResult Result)> ResumeAsync(
        Guid taskId,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        CopyExecutionControl? executionControl = null)
    {
        var task = await repository.GetTaskAsync(taskId, cancellationToken)
            ?? throw new InvalidOperationException("未完成执行关联的任务已不存在。");
        var expandedRequest = PathTemplateExpander.Expand(task.Request, task.Name);
        var request = expandedRequest.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
            ? expandedRequest with { SynchronizationId = task.Id }
            : expandedRequest;
        var normalizedTask = task with { Request = request };
        var plan = await workflow.CreatePlanAsync(request, cancellationToken);
        return await ExecuteAsync(normalizedTask, plan, progress, cancellationToken, executionControl);
    }

    private static bool IsStillCompleted(CopyPlan plan, IncompleteExecutionItem completed)
    {
        var item = plan.Items.FirstOrDefault(candidate =>
            candidate.WillExecute
            && string.Equals(candidate.RelativePath, completed.RelativePath, StringComparison.OrdinalIgnoreCase)
            && candidate.Length == completed.Length
            && candidate.LastWriteTimeUtc == completed.SourceLastWriteTimeUtc);
        if (item is null || item.Action is not (
                CopyAction.CopyNew or CopyAction.Overwrite or CopyAction.RenameCopy
                or CopyAction.ConflictCopy or CopyAction.Verify or CopyAction.CreateDirectory))
        {
            return false;
        }

        if (item.Action == CopyAction.CreateDirectory)
        {
            return Directory.Exists(item.TargetPath);
        }

        if (!File.Exists(item.TargetPath))
        {
            return false;
        }

        var target = new FileInfo(item.TargetPath);
        return target.Length == item.Length
            && Math.Abs((target.LastWriteTimeUtc - item.LastWriteTimeUtc).TotalSeconds) <= 2;
    }
}
