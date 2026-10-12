namespace CopyTools.Domain;

public sealed record VersioningPolicy(
    bool PreserveOverwrittenFiles = false,
    string? VersionsDirectory = null,
    int KeepLatestPerFile = 10,
    TimeSpan? KeepFor = null)
{
    public VersioningPolicy Normalize() => this with
    {
        VersionsDirectory = string.IsNullOrWhiteSpace(VersionsDirectory)
            ? null : Path.GetFullPath(VersionsDirectory),
        KeepLatestPerFile = Math.Clamp(KeepLatestPerFile, 1, 10_000),
        KeepFor = KeepFor is null ? null : TimeSpan.FromDays(Math.Clamp(KeepFor.Value.TotalDays, 1, 36500)),
    };
}
