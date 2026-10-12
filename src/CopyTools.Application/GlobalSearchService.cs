using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record GlobalSearchHit(
    string Kind,
    string Title,
    string Detail,
    DateTime TimestampUtc);

/// <summary>跨任务、运行历史与隔离区做关键词检索，支撑全局搜索框。</summary>
public sealed class GlobalSearchService
{
    private readonly ICopyToolsRepository _repository;

    public GlobalSearchService(ICopyToolsRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var results = new List<GlobalSearchHit>();
        foreach (var task in await _repository.GetTasksAsync(cancellationToken))
        {
            if (Matches(keyword, task.Name, task.Request.SourceDirectory, task.Request.TargetDirectory))
            {
                results.Add(new GlobalSearchHit("任务", task.Name, task.Request.Mode.ToString(), task.UpdatedAtUtc));
            }
        }

        foreach (var run in await _repository.GetRecentRunsAsync(1000, cancellationToken))
        {
            if (Matches(keyword, run.TaskName, run.Status)
                || run.Items.Any(item => Contains(item.RelativePath, keyword)))
            {
                results.Add(new GlobalSearchHit("运行", run.TaskName, run.Status, run.CompletedAtUtc));
            }
        }

        foreach (var entry in await _repository.GetQuarantineEntriesAsync(includeRestored: true, cancellationToken))
        {
            if (Matches(keyword, entry.RelativePath, entry.OriginalPath))
            {
                results.Add(new GlobalSearchHit("隔离区", entry.RelativePath, entry.OriginalPath, entry.QuarantinedAtUtc));
            }
        }

        return results
            .OrderByDescending(hit => hit.TimestampUtc)
            .Take(200)
            .ToArray();
    }

    private static bool Matches(string keyword, params string[] values) =>
        values.Any(value => Contains(value, keyword));

    private static bool Contains(string value, string keyword) =>
        value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
