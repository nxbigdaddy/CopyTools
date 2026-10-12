namespace CopyTools.Domain;

public sealed record FileFilterOptions(
    IReadOnlyList<string>? IncludePatterns = null,
    IReadOnlyList<string>? ExcludePatterns = null,
    IReadOnlyList<string>? ExcludedDirectories = null,
    long? MinimumBytes = null,
    long? MaximumBytes = null,
    DateTime? ModifiedAfterUtc = null,
    DateTime? ModifiedBeforeUtc = null,
    bool IncludeHiddenFiles = false,
    bool IncludeSystemFiles = false,
    ReparsePointPolicy ReparsePointPolicy = ReparsePointPolicy.Skip)
{
    public static FileFilterOptions Empty { get; } = new();

    public IReadOnlyList<string> EffectiveIncludePatterns => IncludePatterns ?? [];

    public IReadOnlyList<string> EffectiveExcludePatterns => ExcludePatterns ?? [];

    public IReadOnlyList<string> EffectiveExcludedDirectories => ExcludedDirectories ?? [];
}
