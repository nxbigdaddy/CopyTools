using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class PlanItemViewModel : ObservableObject
{
    private bool _isSelected;

    public PlanItemViewModel(CopyPlanItem item)
    {
        Item = item;
    }

    public CopyPlanItem Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string RelativePath => Item.RelativePath;

    public string Action => Item.Action switch
    {
        CopyAction.CopyNew => "新增",
        CopyAction.Overwrite => "覆盖",
        CopyAction.SkipExisting => "跳过已有",
        CopyAction.SkipUnchanged => "跳过未变化",
        CopyAction.QuarantineTarget => "隔离目标",
        CopyAction.QuarantineSource => "隔离源端",
        CopyAction.RemoveSource => "清理源文件",
        CopyAction.ConflictCopy => "冲突副本",
        CopyAction.Verify => "校验",
        CopyAction.CreateDirectory => "创建目录",
        CopyAction.QuarantineDirectory => "隔离目录",
        CopyAction.RenameCopy => "重命名复制",
        CopyAction.ConflictNeedsDecision => "等待确认",
        _ => Item.Action.ToString(),
    };

    public string Size => FormatBytes(Item.Length);

    public string Reason => Item.Reason;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
