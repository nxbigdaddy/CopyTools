using CopyTools.Domain;

namespace CopyTools.Application;

/// <summary>在任务成功完成后，按编排配置运行后续任务（任务链）。</summary>
public sealed class TaskChainRunner
{
    private readonly ICopyToolsRepository _repository;
    private readonly ScheduledTaskRunner _runner;

    public TaskChainRunner(ICopyToolsRepository repository, ScheduledTaskRunner runner)
    {
        _repository = repository;
        _runner = runner;
    }

    public async Task<ScheduledRunResult?> RunNextAsync(
        Guid completedTaskId,
        bool succeeded,
        CancellationToken cancellationToken = default)
    {
        if (!succeeded)
        {
            return null;
        }

        var options = await _repository.GetTaskExecutionOptionsAsync(completedTaskId, cancellationToken);
        if (options?.NextTaskId is null)
        {
            return null;
        }

        var predecessor = await _repository.GetTaskAsync(completedTaskId, cancellationToken);
        var successor = await _repository.GetTaskAsync(options.NextTaskId.Value, cancellationToken);
        if (predecessor is null || successor is null)
        {
            return null;
        }

        var requestOverride = options.ChainOutputAsSource
            ? successor.Request with
            {
                SourceDirectory = predecessor.Request.TargetDirectory,
                SourcePaths = null,
            }
            : null;

        var schedule = new TaskSchedule(
            successor.Id,
            true,
            ScheduleTrigger.ApplicationStartup,
            TimeSpan.Zero,
            AllowOverwrite: true,
            AllowQuarantine: true);
        return await _runner.RunAsync(schedule, cancellationToken, requestOverride);
    }
}
