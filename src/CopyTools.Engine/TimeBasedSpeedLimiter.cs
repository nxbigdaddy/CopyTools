using CopyTools.Domain;

namespace CopyTools.Engine;

/// <summary>根据当前本地时间，从分时段规则中解析出应生效的速率上限。</summary>
public sealed class TimeBasedSpeedLimiter
{
    public long? ResolveBytesPerSecond(SpeedSchedule schedule, TimeSpan localTime)
    {
        foreach (var entry in schedule.EffectiveEntries)
        {
            if (Contains(entry, localTime))
            {
                return entry.BytesPerSecond;
            }
        }

        return null;
    }

    private static bool Contains(SpeedScheduleEntry entry, TimeSpan localTime)
    {
        if (entry.StartLocalTime <= entry.EndLocalTime)
        {
            return localTime >= entry.StartLocalTime && localTime < entry.EndLocalTime;
        }

        return localTime >= entry.StartLocalTime || localTime < entry.EndLocalTime;
    }
}
