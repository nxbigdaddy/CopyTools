namespace CopyTools.Domain;

/// <summary>允许执行的时间窗口（本地时间，支持跨午夜）。</summary>
public sealed record TimeWindow(TimeSpan StartLocalTime, TimeSpan EndLocalTime)
{
    public bool Contains(TimeSpan localTime)
    {
        if (StartLocalTime <= EndLocalTime)
        {
            return localTime >= StartLocalTime && localTime < EndLocalTime;
        }

        return localTime >= StartLocalTime || localTime < EndLocalTime;
    }
}
