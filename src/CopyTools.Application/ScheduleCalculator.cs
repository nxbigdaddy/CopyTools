using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class ScheduleCalculator
{
    public DateTime CalculateNextUtc(
        TaskSchedule schedule,
        DateTime afterUtc,
        TimeZoneInfo? timeZone = null)
    {
        var normalized = schedule.Normalize();
        var zone = timeZone ?? TimeZoneInfo.Local;
        var after = EnsureUtc(afterUtc);

        if (normalized.Trigger == ScheduleTrigger.Interval)
        {
            return (normalized.LastTriggeredAtUtc ?? after).Add(normalized.Interval!.Value);
        }

        if (normalized.Trigger == ScheduleTrigger.ApplicationStartup)
        {
            return after;
        }

        var localAfter = TimeZoneInfo.ConvertTimeFromUtc(after, zone);
        var candidateDate = normalized.Trigger switch
        {
            ScheduleTrigger.Daily => localAfter.Date,
            ScheduleTrigger.Weekly => NextWeekday(localAfter.Date, normalized.DayOfWeek),
            ScheduleTrigger.Monthly => DateForMonth(localAfter.Year, localAfter.Month, normalized.DayOfMonth),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule)),
        };
        var localCandidate = DateTime.SpecifyKind(candidateDate + normalized.LocalTime, DateTimeKind.Unspecified);

        if (ConvertToUtc(localCandidate, zone) <= after)
        {
            localCandidate = normalized.Trigger switch
            {
                ScheduleTrigger.Daily => localCandidate.AddDays(1),
                ScheduleTrigger.Weekly => localCandidate.AddDays(7),
                ScheduleTrigger.Monthly => NextMonth(localCandidate, normalized.DayOfMonth),
                _ => localCandidate,
            };
        }

        return ConvertToUtc(localCandidate, zone);
    }

    public bool IsDue(TaskSchedule schedule, DateTime nowUtc) =>
        schedule.Enabled
        && schedule.NextRunAtUtc is not null
        && schedule.NextRunAtUtc.Value <= EnsureUtc(nowUtc);

    public bool ShouldRunMissed(TaskSchedule schedule, DateTime nowUtc) =>
        IsDue(schedule, nowUtc)
        && schedule.MissedRunPolicy == MissedRunPolicy.RunImmediately;

    private static DateTime NextWeekday(DateTime date, DayOfWeek day)
    {
        var offset = ((int)day - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(offset);
    }

    private static DateTime DateForMonth(int year, int month, int requestedDay) =>
        new(year, month, Math.Min(requestedDay, DateTime.DaysInMonth(year, month)));

    private static DateTime NextMonth(DateTime localCandidate, int requestedDay)
    {
        var first = new DateTime(localCandidate.Year, localCandidate.Month, 1).AddMonths(1);
        return DateTime.SpecifyKind(
            DateForMonth(first.Year, first.Month, requestedDay) + localCandidate.TimeOfDay,
            DateTimeKind.Unspecified);
    }

    private static DateTime ConvertToUtc(DateTime local, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
