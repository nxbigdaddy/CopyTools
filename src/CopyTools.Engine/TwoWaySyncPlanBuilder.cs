using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class TwoWaySyncPlanBuilder
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
        var source = sourceFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var target = targetFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var previous = snapshot.ToDictionary(state => state.RelativePath, StringComparer.OrdinalIgnoreCase);
        var pathsToCompare = source.Keys
            .Concat(target.Keys)
            .Concat(previous.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        var items = new List<CopyPlanItem>();
        var conflictSuffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        foreach (var relativePath in pathsToCompare)
        {
            source.TryGetValue(relativePath, out var sourceFile);
            target.TryGetValue(relativePath, out var targetFile);
            previous.TryGetValue(relativePath, out var previousState);

            if (snapshot.Count == 0)
            {
                BuildInitial(items, paths, relativePath, sourceFile, targetFile, conflictSuffix,
                    request.ConflictResolutionPolicy);
                continue;
            }

            var sourceChanged = !SyncStateComparer.MatchesSource(sourceFile, previousState);
            var targetChanged = !SyncStateComparer.MatchesTarget(targetFile, previousState);

            if (!sourceChanged && !targetChanged)
            {
                continue;
            }

            if (sourceChanged && !targetChanged)
            {
                PropagateSide(items, sourceFile, targetFile, relativePath, fromSource: true, paths);
                continue;
            }

            if (!sourceChanged && targetChanged)
            {
                PropagateSide(items, targetFile, sourceFile, relativePath, fromSource: false, paths);
                continue;
            }

            BuildBothChanged(items, paths, relativePath, sourceFile, targetFile, conflictSuffix,
                request.ConflictResolutionPolicy);
        }

        return new CopyPlan(normalizedRequest, items, DateTime.UtcNow);
    }

    private static void BuildInitial(
        ICollection<CopyPlanItem> items,
        ValidatedPaths paths,
        string relativePath,
        FileEntry? source,
        FileEntry? target,
        string conflictSuffix,
        ConflictResolutionPolicy policy)
    {
        if (source is not null && target is null)
        {
            items.Add(Copy(source, Path.Combine(paths.TargetDirectory, relativePath), CopyAction.CopyNew, "首次双向同步：源端新增"));
        }
        else if (source is null && target is not null)
        {
            items.Add(Copy(target, Path.Combine(paths.SourceDirectory, relativePath), CopyAction.CopyNew, "首次双向同步：目标端新增"));
        }
        else if (source is not null && target is not null && !SyncStateComparer.FilesEqual(source, target))
        {
            AddConflict(items, paths, relativePath, source, target, conflictSuffix, policy);
        }
    }

    private static void PropagateSide(
        ICollection<CopyPlanItem> items,
        FileEntry? changedSide,
        FileEntry? otherSide,
        string relativePath,
        bool fromSource,
        ValidatedPaths paths)
    {
        if (changedSide is null)
        {
            if (otherSide is not null)
            {
                items.Add(Quarantine(
                    otherSide,
                    fromSource ? CopyAction.QuarantineTarget : CopyAction.QuarantineSource,
                    fromSource ? "源端删除传播到目标端" : "目标端删除传播到源端"));
            }

            return;
        }

        var destinationRoot = fromSource ? paths.TargetDirectory : paths.SourceDirectory;
        items.Add(Copy(
            changedSide,
            Path.Combine(destinationRoot, relativePath),
            otherSide is null ? CopyAction.CopyNew : CopyAction.Overwrite,
            fromSource ? "源端变化传播到目标端" : "目标端变化传播到源端"));
    }

    private static void BuildBothChanged(
        ICollection<CopyPlanItem> items,
        ValidatedPaths paths,
        string relativePath,
        FileEntry? source,
        FileEntry? target,
        string conflictSuffix,
        ConflictResolutionPolicy policy)
    {
        if (source is null && target is null)
        {
            return;
        }

        if (source is null && target is not null)
        {
            items.Add(Copy(target, Path.Combine(paths.SourceDirectory, relativePath), CopyAction.CopyNew,
                "源端删除但目标端已修改，保留目标端版本"));
            return;
        }

        if (source is not null && target is null)
        {
            items.Add(Copy(source, Path.Combine(paths.TargetDirectory, relativePath), CopyAction.CopyNew,
                "目标端删除但源端已修改，保留源端版本"));
            return;
        }

        if (SyncStateComparer.FilesEqual(source!, target!))
        {
            return;
        }

        AddConflict(items, paths, relativePath, source!, target!, conflictSuffix, policy);
    }

    private static void AddConflict(
        ICollection<CopyPlanItem> items,
        ValidatedPaths paths,
        string relativePath,
        FileEntry source,
        FileEntry target,
        string conflictSuffix,
        ConflictResolutionPolicy policy)
    {
        if (policy == ConflictResolutionPolicy.SourceWins)
        {
            items.Add(Copy(source, Path.Combine(paths.TargetDirectory, relativePath), CopyAction.Overwrite,
                "双方均已修改：按策略保留源端版本") with { ConflictOrigin = ConflictOrigin.Source });
            return;
        }
        if (policy == ConflictResolutionPolicy.TargetWins)
        {
            items.Add(Copy(target, Path.Combine(paths.SourceDirectory, relativePath), CopyAction.Overwrite,
                "双方均已修改：按策略保留目标端版本") with { ConflictOrigin = ConflictOrigin.Target });
            return;
        }
        if (policy == ConflictResolutionPolicy.Ask)
        {
            items.Add(Copy(source, Path.Combine(paths.TargetDirectory, relativePath), CopyAction.ConflictNeedsDecision,
                "双方均已修改：候选源端版本") with { ConflictOrigin = ConflictOrigin.Source });
            items.Add(Copy(target, Path.Combine(paths.SourceDirectory, relativePath), CopyAction.ConflictNeedsDecision,
                "双方均已修改：候选目标端版本") with { ConflictOrigin = ConflictOrigin.Target });
            return;
        }
        var sourceConflictPath = BuildConflictPath(paths.SourceDirectory, relativePath, "target", conflictSuffix);
        var targetConflictPath = BuildConflictPath(paths.TargetDirectory, relativePath, "source", conflictSuffix);
        items.Add(Copy(target, sourceConflictPath, CopyAction.ConflictCopy, "双方均已修改：在源端保留目标版本冲突副本")
            with { ConflictOrigin = ConflictOrigin.Target });
        items.Add(Copy(source, targetConflictPath, CopyAction.ConflictCopy, "双方均已修改：在目标端保留源版本冲突副本")
            with { ConflictOrigin = ConflictOrigin.Source });
    }

    private static string BuildConflictPath(
        string root,
        string relativePath,
        string side,
        string suffix)
    {
        var directory = Path.GetDirectoryName(relativePath);
        var stem = Path.GetFileNameWithoutExtension(relativePath);
        var extension = Path.GetExtension(relativePath);
        var name = $"{stem}.conflict-{side}-{suffix}{extension}";
        return string.IsNullOrEmpty(directory)
            ? Path.Combine(root, name)
            : Path.Combine(root, directory, name);
    }

    private static CopyPlanItem Copy(
        FileEntry source,
        string destinationPath,
        CopyAction action,
        string reason) =>
        new(
            source.RelativePath,
            source.FullPath,
            destinationPath,
            source.Length,
            source.LastWriteTimeUtc,
            action,
            reason)
        {
            SourceAttributes = source.Attributes,
        };

    private static CopyPlanItem Quarantine(FileEntry file, CopyAction action, string reason) =>
        new(
            file.RelativePath,
            file.FullPath,
            file.FullPath,
            file.Length,
            file.LastWriteTimeUtc,
            action,
            reason);
}
