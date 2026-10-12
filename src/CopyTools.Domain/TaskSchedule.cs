namespace CopyTools.Domain;

public sealed record TaskSchedule(
    Guid TaskId,
    bool Enabled,
    ScheduleTrigger Trigger,
    TimeSpan LocalTime,
    DayOfWeek DayOfWeek = DayOfWeek.Monday,
    int DayOfMonth = 1,
    TimeSpan? Interval = null,
    MissedRunPolicy MissedRunPolicy = MissedRunPolicy.RunImmediately,
    bool AllowOverwrite = false,
    bool AllowQuarantine = false,
    DateTime? LastTriggeredAtUtc = null,
    DateTime? NextRunAtUtc = null,
    IReadOnlyList<RunCondition>? Conditions = null,
    TimeWindow? TimeWindow = null)
{
    public IReadOnlyList<RunCondition> EffectiveConditions => Conditions ?? [];
    public TaskSchedule Normalize()
    {
        var localTime = LocalTime < TimeSpan.Zero
            ? TimeSpan.Zero
            : LocalTime >= TimeSpan.FromDays(1)
                ? TimeSpan.FromTicks(LocalTime.Ticks % TimeSpan.TicksPerDay)
                : LocalTime;
        var interval = Interval ?? TimeSpan.FromHours(1);
        interval = TimeSpan.FromMinutes(Math.Clamp(interval.TotalMinutes, 1, 365 * 24 * 60));
        return this with
        {
            LocalTime = localTime,
            DayOfMonth = Math.Clamp(DayOfMonth, 1, 31),
            Interval = interval,
        };
    }
}
