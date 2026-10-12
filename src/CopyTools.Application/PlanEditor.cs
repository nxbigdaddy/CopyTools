using CopyTools.Domain;

namespace CopyTools.Application;

public static class PlanEditor
{
    public static CopyPlan ResolveConflict(
        CopyPlan plan,
        string relativePath,
        ConflictResolutionPolicy policy,
        DateTime? resolvedAtUtc = null)
    {
        var candidates = plan.Items
            .Where(item => item.Action == CopyAction.ConflictNeedsDecision
                && string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 2
            || candidates.All(item => item.ConflictOrigin != ConflictOrigin.Source)
            || candidates.All(item => item.ConflictOrigin != ConflictOrigin.Target))
            throw new InvalidOperationException("未找到完整的双向冲突候选项。");

        var source = candidates.Single(item => item.ConflictOrigin == ConflictOrigin.Source);
        var target = candidates.Single(item => item.ConflictOrigin == ConflictOrigin.Target);
        IReadOnlyList<CopyPlanItem> replacements = policy switch
        {
            ConflictResolutionPolicy.SourceWins =>
                [source with { Action = CopyAction.Overwrite, Reason = "逐项确认：保留源端版本" }],
            ConflictResolutionPolicy.TargetWins =>
                [target with { Action = CopyAction.Overwrite, Reason = "逐项确认：保留目标端版本" }],
            ConflictResolutionPolicy.KeepBoth => BuildKeepBoth(plan, source, target, resolvedAtUtc ?? DateTime.UtcNow),
            _ => throw new ArgumentException("冲突仍未解决。", nameof(policy)),
        };
        var output = plan.Items.Where(item => !candidates.Contains(item)).Concat(replacements).ToArray();
        return plan with { Items = output, CreatedAtUtc = DateTime.UtcNow };
    }

    public static CopyPlan ExcludeItems(CopyPlan plan, IEnumerable<int> itemIndexes)
    {
        var indexes = itemIndexes.ToHashSet();
        if (indexes.Any(index => index < 0 || index >= plan.Items.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(itemIndexes), "排除项索引超出计划范围。");
        }

        return plan with
        {
            Items = plan.Items.Where((_, index) => !indexes.Contains(index)).ToArray(),
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    private static IReadOnlyList<CopyPlanItem> BuildKeepBoth(
        CopyPlan plan, CopyPlanItem source, CopyPlanItem target, DateTime resolvedAtUtc)
    {
        var suffix = resolvedAtUtc.ToString("yyyyMMdd-HHmmss");
        var sourceConflict = ConflictPath(plan.Request.SourceDirectory, source.RelativePath, "target", suffix);
        var targetConflict = ConflictPath(plan.Request.TargetDirectory, source.RelativePath, "source", suffix);
        return
        [
            target with { Action = CopyAction.ConflictCopy, TargetPath = sourceConflict, Reason = "逐项确认：在源端保留目标版本冲突副本" },
            source with { Action = CopyAction.ConflictCopy, TargetPath = targetConflict, Reason = "逐项确认：在目标端保留源版本冲突副本" },
        ];
    }

    private static string ConflictPath(string root, string relativePath, string side, string suffix)
    {
        var directory = Path.GetDirectoryName(relativePath);
        var name = $"{Path.GetFileNameWithoutExtension(relativePath)}.conflict-{side}-{suffix}{Path.GetExtension(relativePath)}";
        return Path.Combine(root, directory ?? string.Empty, name);
    }
}
