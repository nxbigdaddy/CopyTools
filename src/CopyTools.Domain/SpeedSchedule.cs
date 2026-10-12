namespace CopyTools.Domain;

/// <summary>分时段限速条目：在 [Start,End) 本地时间段内应用指定速率；速率 null 表示不限速。</summary>
public sealed record SpeedScheduleEntry(
    TimeSpan StartLocalTime,
    TimeSpan EndLocalTime,
    long? BytesPerSecond);

/// <summary>按本地时间分时段限制传输速率的规则集合。</summary>
public sealed record SpeedSchedule(IReadOnlyList<SpeedScheduleEntry>? Entries = null)
{
    public static SpeedSchedule Empty { get; } = new([]);

    public IReadOnlyList<SpeedScheduleEntry> EffectiveEntries => Entries ?? [];
}
