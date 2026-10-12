using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record ScheduleAdvice(TimeWindow? SuggestedWindow, string Reason);

/// <summary>按历史失败时段给出调度窗口建议。</summary>
public sealed class ScheduleAdvisor
{
    public ScheduleAdvice Advise(IReadOnlyList<RunHistoryEntry> runs)
    {
        var failureHours = runs
            .Where(run => run.Status is "失败" or "已取消")
            .Select(run => run.CompletedAtUtc.Hour)
            .ToArray();
        if (failureHours.Length == 0)
        {
            return new ScheduleAdvice(null, "历史运行未发现明显失败时段，可按需安排。");
        }

        var worstHour = failureHours.GroupBy(hour => hour).OrderByDescending(group => group.Count()).First().Key;
        var start = new TimeSpan((worstHour + 1) % 24, 0, 0);
        return new ScheduleAdvice(
            new TimeWindow(start, start.Add(TimeSpan.FromHours(2))),
            $"历史失败集中在 {worstHour} 点附近，建议避开该时段。");
    }
}
