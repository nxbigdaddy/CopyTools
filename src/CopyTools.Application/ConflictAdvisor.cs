using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record ConflictAdvice(
    string RelativePath,
    ConflictResolutionPolicy Recommendation,
    string Reason);

/// <summary>基于元数据启发式为双向冲突给出解决建议（不自动执行）。</summary>
public sealed class ConflictAdvisor
{
    public ConflictAdvice Advise(
        string relativePath,
        long sourceLength,
        DateTime sourceModifiedUtc,
        long targetLength,
        DateTime targetModifiedUtc)
    {
        var sourceNewer = sourceModifiedUtc > targetModifiedUtc.AddMinutes(5);
        var targetNewer = targetModifiedUtc > sourceModifiedUtc.AddMinutes(5);

        if (sourceNewer && sourceLength != targetLength)
        {
            return new ConflictAdvice(relativePath, ConflictResolutionPolicy.SourceWins, "源端更新时间更晚且大小不同，建议以源端为准。");
        }

        if (targetNewer && sourceLength != targetLength)
        {
            return new ConflictAdvice(relativePath, ConflictResolutionPolicy.TargetWins, "目标端更新时间更晚且大小不同，建议以目标端为准。");
        }

        return new ConflictAdvice(relativePath, ConflictResolutionPolicy.KeepBoth, "两端改动接近，建议保留两份并人工比对。");
    }
}
