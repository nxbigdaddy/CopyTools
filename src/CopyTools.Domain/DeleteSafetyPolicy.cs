namespace CopyTools.Domain;

public sealed record DeleteSafetyPolicy(
    int MaximumItems,
    double MaximumPercentage,
    string? QuarantineDirectory = null)
{
    public static DeleteSafetyPolicy Default { get; } = new(1000, 25);

    public DeleteSafetyPolicy Normalize() => new(
        Math.Clamp(MaximumItems, 1, 1_000_000),
        Math.Clamp(MaximumPercentage, 0.1, 100),
        string.IsNullOrWhiteSpace(QuarantineDirectory)
            ? null
            : Path.GetFullPath(QuarantineDirectory));
}
