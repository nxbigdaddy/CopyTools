using System.Text.Json;
using System.Text.Json.Serialization;
using CopyTools.Domain;

namespace CopyTools.Application;

/// <summary>把早于截止时间的运行历史导出为冷归档 JSON，并在未截断时清理数据库明细。</summary>
public sealed class HistoryArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<int> ArchiveAsync(
        ICopyToolsRepository repository,
        DateTime cutoffUtc,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var runs = await repository.QueryRunsAsync(
            new RunHistoryQuery(ToUtc: cutoffUtc, Limit: 1000),
            cancellationToken);
        if (runs.Count == 0)
        {
            return 0;
        }

        var fullPath = Path.GetFullPath(archivePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, runs, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // 仅在未触及查询上限（说明已完整读取）时清理，避免删除未归档的旧记录。
        if (runs.Count < 1000)
        {
            await repository.DeleteRunsBeforeAsync(cutoffUtc, cancellationToken);
        }

        return runs.Count;
    }
}
