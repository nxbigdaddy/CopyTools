using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record SyncModeAdvice(CopyMode Mode, string Name, string Reason, bool Destructive);

/// <summary>根据用户意图推荐同步模式，并说明删除/覆盖后果。</summary>
public sealed class SyncModeAdvisor
{
    private static readonly IReadOnlyList<SyncModeAdvice> Advice =
    [
        new(CopyMode.IncrementalCopy, "增量复制", "只复制目标端不存在的文件，已存在的不动，最安全。", false),
        new(CopyMode.AccumulateSync, "累加同步", "新增和修改的文件同步过去，源端删除不影响目标历史文件。", false),
        new(CopyMode.OneWaySync, "单向同步", "源端变更单向传播，源端删除会同步删除目标受管副本。", true),
        new(CopyMode.MirrorSync, "镜像同步", "目标最终与源完全一致，目标多出的文件会被移入隔离区。", true),
        new(CopyMode.TwoWaySync, "双向同步", "两端变更互相传播，双方都改过的文件保留两份冲突副本。", true),
        new(CopyMode.MoveSync, "移动同步", "文件复制并校验成功后，把源文件移入隔离区，相当于安全搬家。", true),
        new(CopyMode.UpdateSync, "更新同步", "只保留最新批次，目标端上一批的旧文件会被隔离。", true),
        new(CopyMode.VerifyOnly, "仅校验", "只比较两端差异，不复制、不覆盖、不删除。", false),
    ];

    public SyncModeAdvice Advise(string intent)
    {
        var lower = intent.Trim();
        return Advice.FirstOrDefault(item =>
                lower.Contains(item.Name, StringComparison.OrdinalIgnoreCase)
                || lower.Contains(item.Reason[..Math.Min(4, item.Reason.Length)], StringComparison.OrdinalIgnoreCase))
            ?? AdviseByKeyword(lower);
    }

    public IReadOnlyList<SyncModeAdvice> All() => Advice;

    private static SyncModeAdvice AdviseByKeyword(string intent)
    {
        if (intent.Contains("一致", StringComparison.Ordinal) || intent.Contains("镜像", StringComparison.Ordinal))
            return Advice.Single(item => item.Mode == CopyMode.MirrorSync);
        if (intent.Contains("双向", StringComparison.Ordinal))
            return Advice.Single(item => item.Mode == CopyMode.TwoWaySync);
        if (intent.Contains("搬家", StringComparison.Ordinal) || intent.Contains("清空源", StringComparison.Ordinal))
            return Advice.Single(item => item.Mode == CopyMode.MoveSync);
        if (intent.Contains("追加", StringComparison.Ordinal) || intent.Contains("新增", StringComparison.Ordinal))
            return Advice.Single(item => item.Mode == CopyMode.IncrementalCopy);
        if (intent.Contains("备份", StringComparison.Ordinal))
            return Advice.Single(item => item.Mode == CopyMode.AccumulateSync);
        return Advice.Single(item => item.Mode == CopyMode.IncrementalCopy);
    }
}
