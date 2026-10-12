using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed record AutoTriggerRuleViewModel(SavedCopyTask Task, AutoTriggerRule Rule)
{
    public string Name => Task.Name;

    public string Kind => Rule.Kind == AutoTriggerKind.FileChange ? "文件变化" : "路径/设备可用";

    public string Enabled => Rule.Enabled ? "启用" : "停用";

    public string Delay => $"{Rule.DebounceDelay.TotalSeconds:0} 秒";

    public string Safety => $"覆盖：{(Rule.AllowOverwrite ? "允许" : "需确认")}；"
        + $"隔离：{(Rule.AllowQuarantine ? "允许" : "需确认")}";
}
