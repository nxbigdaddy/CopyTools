namespace CopyTools.Domain;

public sealed record RetryPolicy(int MaxAttempts, TimeSpan Delay, TimeSpan? AvailabilityWaitTimeout = null)
{
    public static RetryPolicy Default { get; } = new(3, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));

    public RetryPolicy Normalize() => new(
        Math.Clamp(MaxAttempts, 1, 10),
        TimeSpan.FromMilliseconds(Math.Clamp(Delay.TotalMilliseconds, 0, 60_000)),
        TimeSpan.FromSeconds(Math.Clamp((AvailabilityWaitTimeout ?? TimeSpan.FromMinutes(2)).TotalSeconds, 0, 86_400)));
}
