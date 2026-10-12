using System.Collections.Concurrent;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class TaskQueueService(
    ICopyToolsRepository repository,
    ScheduledTaskRunner runner,
    ControlledCommandRunner? commandRunner = null)
{
    private readonly ControlledCommandRunner _commandRunner = commandRunner ?? new ControlledCommandRunner();

    public async Task<TaskQueueResult> RunAsync(
        IReadOnlyCollection<Guid> taskIds,
        int maximumParallelTasks = 1,
        CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var uniqueIds = taskIds.Distinct().ToHashSet();
        var tasks = (await repository.GetTasksAsync(cancellationToken))
            .Where(task => uniqueIds.Contains(task.Id)).ToDictionary(task => task.Id);
        var allOptions = (await repository.GetAllTaskExecutionOptionsAsync(cancellationToken))
            .ToDictionary(options => options.TaskId, options => options.Normalize());
        var options = tasks.Keys.ToDictionary(
            id => id,
            id => allOptions.GetValueOrDefault(id) ?? new TaskExecutionOptions(id));
        ValidateDependencyGraph(tasks.Keys, options);
        var selectedIds = tasks.Keys.ToHashSet();
        var externalDependencySuccess = new Dictionary<Guid, bool>();
        foreach (var dependencyId in options.Values.SelectMany(item => item.EffectiveDependencies)
                     .Where(id => !selectedIds.Contains(id)).Distinct())
        {
            var history = await repository.GetLatestRunForTaskAsync(dependencyId, cancellationToken);
            externalDependencySuccess[dependencyId] = history?.Status == "成功";
        }

        var results = new ConcurrentDictionary<Guid, TaskQueueItemResult>();
        var remaining = tasks.Keys.ToHashSet();
        var running = new Dictionary<Guid, Task<TaskQueueItemResult>>();
        var limit = Math.Clamp(maximumParallelTasks, 1, 16);

        while (remaining.Count > 0 || running.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var taskId in remaining
                         .Where(id => DependenciesResolved(id, options, results, externalDependencySuccess))
                         .OrderByDescending(id => options[id].Priority)
                         .ThenBy(id => tasks[id].Name, StringComparer.OrdinalIgnoreCase)
                         .Take(limit - running.Count)
                         .ToArray())
            {
                remaining.Remove(taskId);
                running[taskId] = RunOneAsync(tasks[taskId], options[taskId], cancellationToken);
            }

            foreach (var taskId in remaining
                         .Where(id => HasFailedDependency(id, options, results, externalDependencySuccess))
                         .ToArray())
            {
                remaining.Remove(taskId);
                var now = DateTime.UtcNow;
                results[taskId] = new TaskQueueItemResult(
                    taskId, tasks[taskId].Name, "依赖失败", now, now, "前置任务未成功，已跳过。");
            }

            if (running.Count == 0)
            {
                foreach (var taskId in remaining.ToArray())
                {
                    remaining.Remove(taskId);
                    var now = DateTime.UtcNow;
                    results[taskId] = new TaskQueueItemResult(
                        taskId, tasks[taskId].Name, "依赖缺失", now, now, "前置任务不在本次队列且没有成功历史。");
                }

                break;
            }

            var completed = await Task.WhenAny(running.Values);
            var pair = running.Single(item => item.Value == completed);
            running.Remove(pair.Key);
            results[pair.Key] = await completed;
        }

        return new TaskQueueResult(
            started,
            DateTime.UtcNow,
            tasks.Keys.Select(id => results[id])
                .OrderBy(item => item.StartedAtUtc).ThenBy(item => item.TaskName).ToArray());
    }

    private async Task<TaskQueueItemResult> RunOneAsync(
        SavedCopyTask task,
        TaskExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        if (options.BeforeCommand is { } before)
        {
            var result = await _commandRunner.RunAsync(before, cancellationToken);
            if (!result.Successful)
            {
                return new TaskQueueItemResult(
                    task.Id, task.Name, "前置命令失败", started, DateTime.UtcNow,
                    DescribeCommandFailure(result));
            }
        }

        var schedule = await repository.GetScheduleAsync(task.Id, cancellationToken)
            ?? new TaskSchedule(task.Id, true, ScheduleTrigger.ApplicationStartup, TimeSpan.Zero);
        var execution = await runner.RunAsync(schedule with
        {
            Enabled = true,
            AllowOverwrite = true,
            AllowQuarantine = true,
        }, cancellationToken);

        if (options.AfterCommand is { } after
            && (execution.Status == "成功" || options.RunAfterCommandOnFailure))
        {
            var result = await _commandRunner.RunAsync(after, cancellationToken);
            if (!result.Successful)
            {
                return new TaskQueueItemResult(
                    task.Id, task.Name, "后置命令失败", started, DateTime.UtcNow,
                    DescribeCommandFailure(result));
            }
        }

        return new TaskQueueItemResult(
            task.Id, task.Name, execution.Status, started, DateTime.UtcNow, execution.Message);
    }

    private static bool DependenciesResolved(
        Guid taskId,
        IReadOnlyDictionary<Guid, TaskExecutionOptions> options,
        IReadOnlyDictionary<Guid, TaskQueueItemResult> results,
        IReadOnlyDictionary<Guid, bool> externalDependencySuccess) =>
        options[taskId].EffectiveDependencies.All(id =>
            results.TryGetValue(id, out var result)
                ? result.Status == "成功"
                : externalDependencySuccess.GetValueOrDefault(id));

    private static bool HasFailedDependency(
        Guid taskId,
        IReadOnlyDictionary<Guid, TaskExecutionOptions> options,
        IReadOnlyDictionary<Guid, TaskQueueItemResult> results,
        IReadOnlyDictionary<Guid, bool> externalDependencySuccess) =>
        options[taskId].EffectiveDependencies.Any(id =>
            results.TryGetValue(id, out var result)
                ? result.Status != "成功"
                : externalDependencySuccess.TryGetValue(id, out var succeeded) && !succeeded);

    private static void ValidateDependencyGraph(
        IEnumerable<Guid> taskIds,
        IReadOnlyDictionary<Guid, TaskExecutionOptions> options)
    {
        var set = taskIds.ToHashSet();
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        foreach (var id in set)
        {
            Visit(id);
        }

        void Visit(Guid id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException("任务依赖关系存在循环。");
            foreach (var dependency in options[id].EffectiveDependencies.Where(set.Contains)) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static string DescribeCommandFailure(ControlledCommandResult result) => result.TimedOut
        ? "命令执行超时。"
        : $"命令退出码 {result.ExitCode?.ToString() ?? "未知"}。";
}
