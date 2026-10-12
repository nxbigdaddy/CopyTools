using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class QuarantineService
{
    public QuarantineOperationResult Quarantine(
        CopyPlan plan,
        CopyPlanItem item,
        DateTime operationTimeUtc)
    {
        if (item.Action is not CopyAction.QuarantineTarget
            and not CopyAction.QuarantineSource
            and not CopyAction.RemoveSource)
        {
            throw new ArgumentException("计划项不是隔离操作。", nameof(item));
        }

        ValidateTargetUnchanged(item);
        var root = ResolveQuarantineRoot(plan.Request.EffectiveDeleteSafetyPolicy);
        var batchDirectory = Path.Combine(
            root,
            operationTimeUtc.ToString("yyyyMMdd-HHmmss"),
            Guid.NewGuid().ToString("N"));
        var quarantinePath = Path.Combine(batchDirectory, item.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)!);
        File.Move(item.SourcePath, quarantinePath, overwrite: false);

        return new QuarantineOperationResult(
            item.SourcePath,
            quarantinePath,
            item.RelativePath,
            item.Length,
            operationTimeUtc);
    }

    public void Restore(QuarantineEntry entry, bool overwrite)
    {
        if (!File.Exists(entry.QuarantinePath))
        {
            throw new FileNotFoundException("隔离文件不存在，可能已被手动移动或删除。", entry.QuarantinePath);
        }

        var originalDirectory = Path.GetDirectoryName(entry.OriginalPath)
            ?? throw new InvalidOperationException("无法确定原始目录。");
        Directory.CreateDirectory(originalDirectory);

        if (File.Exists(entry.OriginalPath) && !overwrite)
        {
            throw new IOException("原始位置已经存在同名文件，未执行覆盖恢复。");
        }

        File.Move(entry.QuarantinePath, entry.OriginalPath, overwrite);
    }

    private static string ResolveQuarantineRoot(DeleteSafetyPolicy policy)
    {
        var normalized = policy.Normalize();
        if (normalized.QuarantineDirectory is not null)
        {
            return normalized.QuarantineDirectory;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopyTools",
            "quarantine");
    }

    private static void ValidateTargetUnchanged(CopyPlanItem item)
    {
        if (!File.Exists(item.SourcePath))
        {
            throw new IOException("待隔离文件在生成预览后已不存在，请重新生成预览。");
        }

        var current = new FileInfo(item.SourcePath);
        if (current.Length != item.Length
            || Math.Abs((current.LastWriteTimeUtc - item.LastWriteTimeUtc).TotalSeconds) > 2)
        {
            throw new IOException("待隔离文件在生成预览后已发生变化，请重新生成预览。");
        }
    }
}

public sealed record QuarantineOperationResult(
    string OriginalPath,
    string QuarantinePath,
    string RelativePath,
    long Length,
    DateTime QuarantinedAtUtc);
