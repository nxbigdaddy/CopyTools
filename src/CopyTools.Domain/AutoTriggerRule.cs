namespace CopyTools.Domain;

public sealed record AutoTriggerRule(
    Guid TaskId,
    AutoTriggerKind Kind,
    bool Enabled,
    TimeSpan DebounceDelay,
    bool AllowOverwrite = false,
    bool AllowQuarantine = false,
    TimeSpan? MinIntervalBetweenRuns = null)
{
    public AutoTriggerRule Normalize() => this with
    {
        DebounceDelay = TimeSpan.FromSeconds(Math.Clamp(DebounceDelay.TotalSeconds, 1, 3600)),
        MinIntervalBetweenRuns = MinIntervalBetweenRuns is null
            ? null
            : TimeSpan.FromSeconds(Math.Clamp(MinIntervalBetweenRuns.Value.TotalSeconds, 0, 86400)),
    };
}
