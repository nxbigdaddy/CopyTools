using CopyTools.Domain;

namespace CopyTools.Application;

public static class RunHistoryFactory
{
    public static RunHistoryEntry FromExecution(
        SavedCopyTask task,
        CopyPlan plan,
        CopyExecutionResult result)
    {
        var items = result.EffectiveItems
            .Select(item => new RunHistoryItem(
                item.RelativePath,
                item.Action,
                item.Result,
                item.Bytes,
                item.ErrorMessage,
                item.Attempts))
            .ToArray();
        var runId = Guid.NewGuid();
        var quarantineEntries = result.EffectiveQuarantinedFiles
            .Select(file => new QuarantineEntry(
                Guid.NewGuid(),
                runId,
                file.OriginalPath,
                file.QuarantinePath,
                file.RelativePath,
                file.Length,
                file.QuarantinedAtUtc,
                null))
            .ToArray();
        return new RunHistoryEntry(
            runId,
            task.Id,
            task.Name,
            plan.Request,
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.Status,
            result.SuccessfulFiles,
            result.SkippedFiles,
            result.Failures.Count,
            result.CopiedBytes,
            items,
            quarantineEntries);
    }

    public static RunHistoryEntry Blocked(
        SavedCopyTask task,
        CopyPlan plan,
        string reason,
        DateTime nowUtc) =>
        new(
            Guid.NewGuid(),
            task.Id,
            task.Name,
            plan.Request,
            nowUtc,
            nowUtc,
            "等待人工确认",
            0,
            plan.SkippedFiles,
            0,
            0,
            plan.Items.Select(item => new RunHistoryItem(
                item.RelativePath,
                item.Action,
                "未执行",
                0,
                reason,
                0)).ToArray());
}
