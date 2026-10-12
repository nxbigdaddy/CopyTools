namespace CopyTools.Domain;

public sealed record PerformanceOptions(
    int BufferSizeBytes,
    long? SpeedLimitBytesPerSecond = null,
    int MaxParallelFiles = 1,
    bool AutoTune = true,
    bool BackgroundLowImpact = false)
{
    public static PerformanceOptions Default { get; } = new(1024 * 1024, MaxParallelFiles: 4);

    public PerformanceOptions Normalize() => new(
        Math.Clamp(BufferSizeBytes, 64 * 1024, 16 * 1024 * 1024),
        SpeedLimitBytesPerSecond is null
            ? null
            : Math.Clamp(SpeedLimitBytesPerSecond.Value, 64 * 1024, 10L * 1024 * 1024 * 1024),
        Math.Clamp(MaxParallelFiles, 1, 16),
        AutoTune,
        BackgroundLowImpact);
}
