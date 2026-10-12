using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class CopyPlanBuilder
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    public CopyPlan Build(
        CopyTaskRequest request,
        ValidatedPaths paths,
        IReadOnlyList<FileEntry> sourceFiles,
        IReadOnlyList<FileEntry>? targetFiles = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        var normalizedRequest = request with
        {
            SourceDirectory = paths.SourceDirectory,
            TargetDirectory = paths.TargetDirectory,
        };

        var items = new List<CopyPlanItem>();
        foreach (var source in sourceFiles)
        {
            items.Add(BuildItem(normalizedRequest, source));
            if (normalizedRequest.Mode == CopyMode.MoveSync)
            {
                items.Add(CreateItem(
                    source,
                    Path.Combine(normalizedRequest.TargetDirectory, source.RelativePath),
                    CopyAction.RemoveSource,
                    "目标文件复制并校验成功后清理源文件"));
            }
        }

        if (normalizedRequest.Mode == CopyMode.MirrorSync)
        {
            var sourcePaths = sourceFiles
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targetFiles ?? [])
            {
                if (!sourcePaths.Contains(target.RelativePath))
                {
                    items.Add(new CopyPlanItem(
                        target.RelativePath,
                        target.FullPath,
                        target.FullPath,
                        target.Length,
                        target.LastWriteTimeUtc,
                        CopyAction.QuarantineTarget,
                        "目标文件在源目录中不存在，将移入隔离区"));
                }
            }
        }

        return new CopyPlan(normalizedRequest, items, DateTime.UtcNow);
    }

    private static CopyPlanItem BuildItem(CopyTaskRequest request, FileEntry source)
    {
        var targetPath = Path.Combine(request.TargetDirectory, source.RelativePath);

        if (request.Mode == CopyMode.VerifyOnly)
        {
            return CreateItem(
                source,
                targetPath,
                CopyAction.Verify,
                "仅校验源文件和目标文件，不写入数据");
        }

        if (!File.Exists(targetPath))
        {
            return CreateItem(source, targetPath, CopyAction.CopyNew, "目标文件不存在");
        }

        var target = new FileInfo(targetPath);
        var unchanged = source.ContentHash is not null
            ? string.Equals(source.ContentHash, ComputeSha256(targetPath), StringComparison.OrdinalIgnoreCase)
            : source.Length == target.Length
            && Math.Abs((source.LastWriteTimeUtc - target.LastWriteTimeUtc).TotalSeconds)
                <= TimestampTolerance.TotalSeconds;

        if (request.ExistingFilePolicy is not ExistingFilePolicy.ModeDefault)
        {
            return request.ExistingFilePolicy switch
            {
                ExistingFilePolicy.Overwrite or ExistingFilePolicy.Ask => CreateItem(
                    source, targetPath, CopyAction.Overwrite,
                    request.ExistingFilePolicy == ExistingFilePolicy.Ask
                        ? "同名文件需要执行前确认"
                        : "同名文件策略：覆盖"),
                ExistingFilePolicy.Skip => CreateItem(
                    source, targetPath, CopyAction.SkipExisting, "同名文件策略：跳过"),
                ExistingFilePolicy.Rename => CreateItem(
                    source, BuildRenamedTargetPath(targetPath), CopyAction.RenameCopy,
                    "同名文件策略：自动重命名"),
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
        }

        return request.Mode switch
        {
            CopyMode.FullCopy => CreateItem(
                source,
                targetPath,
                CopyAction.Overwrite,
                unchanged ? "普通复制将覆盖已有文件" : "目标文件已存在"),
            CopyMode.IncrementalCopy => CreateItem(
                source,
                targetPath,
                CopyAction.SkipExisting,
                "增量复制只复制目标不存在的文件"),
            CopyMode.AccumulateSync when unchanged => CreateItem(
                source,
                targetPath,
                CopyAction.SkipUnchanged,
                "文件大小和修改时间未变化"),
            CopyMode.AccumulateSync => CreateItem(
                source,
                targetPath,
                CopyAction.Overwrite,
                "源文件已发生变化"),
            CopyMode.MirrorSync when unchanged => CreateItem(
                source,
                targetPath,
                CopyAction.SkipUnchanged,
                "镜像中的文件未变化"),
            CopyMode.MirrorSync => CreateItem(
                source,
                targetPath,
                CopyAction.Overwrite,
                "镜像目标需要更新"),
            CopyMode.MoveSync when unchanged => CreateItem(
                source,
                targetPath,
                CopyAction.SkipUnchanged,
                "目标已有相同文件，只需清理源文件"),
            CopyMode.MoveSync => CreateItem(
                source,
                targetPath,
                CopyAction.Overwrite,
                "移动前先更新并校验目标文件"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "不支持的复制模式。"),
        };
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static CopyPlanItem CreateItem(
        FileEntry source,
        string targetPath,
        CopyAction action,
        string reason) =>
        new(
            source.RelativePath,
            source.FullPath,
            targetPath,
            source.Length,
            source.LastWriteTimeUtc,
            action,
            reason)
        {
            SourceAttributes = source.Attributes,
        };

    private static string BuildRenamedTargetPath(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        var stem = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法为同名文件生成可用的新名称。");
    }
}
