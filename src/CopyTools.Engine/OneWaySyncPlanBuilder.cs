using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class OneWaySyncPlanBuilder
{
    public CopyPlan Build(
        CopyTaskRequest request,
        ValidatedPaths paths,
        IReadOnlyList<FileEntry> sourceFiles,
        IReadOnlyList<FileEntry> targetFiles,
        IReadOnlyList<SyncFileState> snapshot)
    {
        var normalized = request with { SourceDirectory = paths.SourceDirectory, TargetDirectory = paths.TargetDirectory };
        var source = sourceFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var target = targetFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var previous = snapshot.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var items = new List<CopyPlanItem>();

        foreach (var file in sourceFiles)
        {
            var targetPath = Path.Combine(paths.TargetDirectory, file.RelativePath);
            if (!target.TryGetValue(file.RelativePath, out var targetFile))
                items.Add(Copy(file, targetPath, CopyAction.CopyNew, "源端新增文件"));
            else if (!SyncStateComparer.FilesEqual(file, targetFile))
                items.Add(Copy(file, targetPath, CopyAction.Overwrite, "源端新增或修改传播到目标端"));
            else
                items.Add(Copy(file, targetPath, CopyAction.SkipUnchanged, "源端与目标端文件未变化"));
        }

        // 只传播上次成功同步时由任务管理、且本次确实从源端删除的路径。
        // 目标端自行新增的文件不在快照的源端集合中，因此会保留。
        foreach (var previousFile in previous.Values.Where(state => state.SourceExists && !source.ContainsKey(state.RelativePath)))
        {
            if (target.TryGetValue(previousFile.RelativePath, out var targetFile))
                items.Add(new CopyPlanItem(targetFile.RelativePath, targetFile.FullPath, targetFile.FullPath,
                    targetFile.Length, targetFile.LastWriteTimeUtc, CopyAction.QuarantineTarget,
                    "源端删除传播：目标文件移入隔离区"));
        }

        return new CopyPlan(normalized, items, DateTime.UtcNow);
    }

    private static CopyPlanItem Copy(FileEntry source, string target, CopyAction action, string reason) =>
        new(source.RelativePath, source.FullPath, target, source.Length, source.LastWriteTimeUtc, action, reason)
        { SourceAttributes = source.Attributes };
}
