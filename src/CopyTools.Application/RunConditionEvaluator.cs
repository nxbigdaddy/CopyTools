using CopyTools.Domain;

namespace CopyTools.Application;

/// <summary>评估计划/自动任务执行前的门控条件。</summary>
public sealed class RunConditionEvaluator
{
    public async Task<RunConditionEvaluation> EvaluateAsync(
        IReadOnlyList<RunCondition> conditions,
        SavedCopyTask task,
        ICopyToolsRepository repository,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        foreach (var condition in conditions)
        {
            var reason = await EvaluateOneAsync(condition, task, repository, cancellationToken);
            if (reason is not null)
            {
                failures.Add(reason);
            }
        }

        return failures.Count == 0
            ? new RunConditionEvaluation(true, null)
            : new RunConditionEvaluation(false, string.Join("；", failures));
    }

    private static async Task<string?> EvaluateOneAsync(
        RunCondition condition,
        SavedCopyTask task,
        ICopyToolsRepository repository,
        CancellationToken cancellationToken)
    {
        switch (condition.Kind)
        {
            case RunConditionKind.None:
                return null;

            case RunConditionKind.MinimumDiskSpaceGb:
                return EvaluateDiskSpace(condition, task);

            case RunConditionKind.FileExists:
                return File.Exists(condition.Argument)
                    ? null
                    : $"前置文件不存在：{condition.Argument}。";

            case RunConditionKind.PreviousRunSucceeded:
                var latest = await repository.GetLatestRunForTaskAsync(task.Id, cancellationToken);
                return latest is null || latest.Status is "成功" or "部分成功"
                    ? null
                    : "上次运行未成功。";

            default:
                return null;
        }
    }

    private static string? EvaluateDiskSpace(RunCondition condition, SavedCopyTask task)
    {
        if (!double.TryParse(condition.Argument, out var requiredGb) || requiredGb <= 0)
        {
            return null;
        }

        string root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(task.Request.TargetDirectory)) ?? task.Request.TargetDirectory;
        }
        catch (ArgumentException)
        {
            return null;
        }

        DriveInfo drive;
        try
        {
            drive = new DriveInfo(root);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var availableGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
        return availableGb >= requiredGb
            ? null
            : $"目标磁盘可用空间 {availableGb:0.##} GB 不足 {requiredGb:0.##} GB。";
    }
}
