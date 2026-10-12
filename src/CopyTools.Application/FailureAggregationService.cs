using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record FailureCause(string Category, int Count, string SamplePath);

/// <summary>按错误类型聚合历史失败，输出共性问题与样本，支撑故障分析。</summary>
public sealed class FailureAggregationService
{
    public IReadOnlyList<FailureCause> Aggregate(
        IReadOnlyList<RunHistoryEntry> runs,
        int top = 10)
    {
        return runs
            .SelectMany(run => run.Items.Where(item => item.ErrorMessage is not null))
            .GroupBy(item => FailureCategorizer.Categorize(item.ErrorMessage!), StringComparer.OrdinalIgnoreCase)
            .Select(group => new FailureCause(group.Key, group.Count(), group.First().RelativePath))
            .OrderByDescending(cause => cause.Count)
            .Take(Math.Max(1, top))
            .ToArray();
    }
}
