using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed record ScheduledTaskViewModel(SavedCopyTask Task, TaskSchedule Schedule)
{
    public string Name => Task.Name;

    public string Trigger => Schedule.Trigger switch
    {
        ScheduleTrigger.Daily => "每天",
        ScheduleTrigger.Weekly => "每周",
        ScheduleTrigger.Monthly => "每月",
        ScheduleTrigger.Interval => $"每 {Schedule.Interval?.TotalMinutes:0} 分钟",
        ScheduleTrigger.ApplicationStartup => "应用启动时",
        _ => Schedule.Trigger.ToString(),
    };

    public string Enabled => Schedule.Enabled ? "启用" : "停用";

    public string NextRun => Schedule.NextRunAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "等待计算";

    public string Safety => $"覆盖：{(Schedule.AllowOverwrite ? "允许" : "需确认")}；"
        + $"隔离：{(Schedule.AllowQuarantine ? "允许" : "需确认")}";
}
