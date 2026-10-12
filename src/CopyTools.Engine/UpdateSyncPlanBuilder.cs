using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class UpdateSyncPlanBuilder
{
    public CopyPlan Build(
        CopyTaskRequest request,
        ValidatedPaths paths,
        IReadOnlyList<FileEntry> sourceFiles,
        IReadOnlyList<FileEntry> targetFiles,
        IReadOnlyList<SyncFileState> snapshot)
    {
        var normalizedRequest = request with
        {
            SourceDirectory = paths.SourceDirectory,
            TargetDirectory = paths.TargetDirectory,
        };
        var previous = snapshot.ToDictionary(state => state.RelativePath, StringComparer.OrdinalIgnoreCase);
        var target = targetFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var changedSource = snapshot.Count == 0
            ? sourceFiles.ToArray()
            : sourceFiles
                .Where(file => !previous.TryGetValue(file.RelativePath, out var state)
                    || !SyncStateComparer.MatchesSource(file, state))
                .ToArray();

        if (changedSource.Length == 0)
        {
            return new CopyPlan(normalizedRequest, [], DateTime.UtcNow);
        }

        var changedPaths = changedSource
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = new List<CopyPlanItem>();

        foreach (var sourceFile in changedSource)
        {
            var targetPath = Path.Combine(paths.TargetDirectory, sourceFile.RelativePath);
            items.Add(new CopyPlanItem(
                sourceFile.RelativePath,
                sourceFile.FullPath,
                targetPath,
                sourceFile.Length,
                sourceFile.LastWriteTimeUtc,
                target.ContainsKey(sourceFile.RelativePath) ? CopyAction.Overwrite : CopyAction.CopyNew,
                target.ContainsKey(sourceFile.RelativePath) ? "本批次文件覆盖目标版本" : "本批次新增文件")
            {
                SourceAttributes = sourceFile.Attributes,
            });
        }

        // 先把新批次完整复制到目标，再隔离上一批次的其余文件。
        // 即使后续隔离失败，目标端也已经具备全部新批次文件。
        foreach (var targetFile in targetFiles)
        {
            if (!changedPaths.Contains(targetFile.RelativePath))
            {
                items.Add(Quarantine(targetFile, "更新同步将上一批次文件移入隔离区"));
            }
        }

        return new CopyPlan(normalizedRequest, items, DateTime.UtcNow);
    }

    private static CopyPlanItem Quarantine(FileEntry target, string reason) =>
        new(
            target.RelativePath,
            target.FullPath,
            target.FullPath,
            target.Length,
            target.LastWriteTimeUtc,
            CopyAction.QuarantineTarget,
            reason);
}
