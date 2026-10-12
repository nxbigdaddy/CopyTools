namespace CopyTools.Domain;

public sealed record ControlledCommand(
    string FileName,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null,
    IReadOnlyList<int>? SuccessExitCodes = null)
{
    public IReadOnlyList<string> EffectiveArguments => Arguments ?? [];
    public TimeSpan EffectiveTimeout => Timeout is { } value
        ? TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds, 1, 24 * 60 * 60))
        : TimeSpan.FromMinutes(10);
    public IReadOnlySet<int> EffectiveSuccessExitCodes => (SuccessExitCodes ?? [0]).ToHashSet();
}
