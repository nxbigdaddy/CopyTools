using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record DurationEstimate(TimeSpan Estimated, double BytesPerSecond, bool InsufficientData);

/// <summary>按历史吞吐率估算本次任务耗时。</summary>
public sealed class DurationEstimator
{
    public DurationEstimate Estimate(IReadOnlyList<RunHistoryEntry> runs, long totalBytes)
    {
        var completed = runs
            .Where(run => run.CopiedBytes > 0 && run.CompletedAtUtc > run.StartedAtUtc)
            .ToArray();
        if (completed.Length == 0 || totalBytes <= 0)
        {
            return new DurationEstimate(TimeSpan.Zero, 0, true);
        }

        var totalSeconds = completed.Sum(run => (run.CompletedAtUtc - run.StartedAtUtc).TotalSeconds);
        var bytesPerSecond = totalSeconds <= 0
            ? 0
            : completed.Sum(run => run.CopiedBytes) / totalSeconds;
        if (bytesPerSecond <= 0)
        {
            return new DurationEstimate(TimeSpan.Zero, 0, true);
        }

        return new DurationEstimate(TimeSpan.FromSeconds(totalBytes / bytesPerSecond), bytesPerSecond, false);
    }
}
