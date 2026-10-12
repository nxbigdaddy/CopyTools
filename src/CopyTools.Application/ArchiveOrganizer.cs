namespace CopyTools.Application;

public sealed record ArchiveMoveSuggestion(string RelativePath, string SuggestedDirectory, string Reason);

/// <summary>把文件按类别/日期生成归档移动建议（移动仍走现有安全执行链）。</summary>
public sealed class ArchiveOrganizer
{
    public IReadOnlyList<ArchiveMoveSuggestion> Suggest(
        IReadOnlyList<string> relativePaths,
        string archiveRoot = "归档")
    {
        var classifier = new FileClassifier();
        return relativePaths
            .Select(path =>
            {
                var category = classifier.Classify(path).Category;
                return new ArchiveMoveSuggestion(
                    path,
                    Path.Combine(archiveRoot, category),
                    $"按类别归入「{category}」");
            })
            .ToArray();
    }
}
