using System.IO.Enumeration;
using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class FileFilterMatcher
{
    public bool IsMatch(FileInfo file, string relativePath, FileFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IncludeHiddenFiles && file.Attributes.HasFlag(FileAttributes.Hidden))
        {
            return false;
        }

        if (!options.IncludeSystemFiles && file.Attributes.HasFlag(FileAttributes.System))
        {
            return false;
        }

        if (options.MinimumBytes is not null && file.Length < options.MinimumBytes.Value)
        {
            return false;
        }

        if (options.MaximumBytes is not null && file.Length > options.MaximumBytes.Value)
        {
            return false;
        }

        if (options.ModifiedAfterUtc is not null && file.LastWriteTimeUtc < options.ModifiedAfterUtc.Value)
        {
            return false;
        }

        if (options.ModifiedBeforeUtc is not null && file.LastWriteTimeUtc > options.ModifiedBeforeUtc.Value)
        {
            return false;
        }

        if (ContainsExcludedDirectory(relativePath, options.EffectiveExcludedDirectories))
        {
            return false;
        }

        var includePatterns = NormalizePatterns(options.EffectiveIncludePatterns);
        if (includePatterns.Count > 0 && !includePatterns.Any(pattern => Matches(pattern, file.Name, relativePath)))
        {
            return false;
        }

        var excludePatterns = NormalizePatterns(options.EffectiveExcludePatterns);
        return !excludePatterns.Any(pattern => Matches(pattern, file.Name, relativePath));
    }

    private static bool ContainsExcludedDirectory(
        string relativePath,
        IReadOnlyList<string> excludedDirectories)
    {
        if (excludedDirectories.Count == 0)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        var segments = directory.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var patterns = NormalizePatterns(excludedDirectories);
        return segments.Any(segment =>
            patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, segment, ignoreCase: true)));
    }

    private static bool Matches(string pattern, string fileName, string relativePath) =>
        FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true)
        || FileSystemName.MatchesSimpleExpression(pattern, relativePath, ignoreCase: true);

    private static IReadOnlyList<string> NormalizePatterns(IReadOnlyList<string> patterns) =>
        patterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
