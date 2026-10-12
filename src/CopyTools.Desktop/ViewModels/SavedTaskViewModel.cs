using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class SavedTaskViewModel(
    SavedCopyTask task,
    TaskExecutionOptions? options = null,
    RunHistoryEntry? latestRun = null)
{
    public SavedCopyTask Task { get; } = task;
    public TaskExecutionOptions ExecutionOptions { get; } = options ?? new TaskExecutionOptions(task.Id);
    public bool IsQueueSelected { get; set; }
    public string Name => Task.Name;

    public string Mode => Task.Request.Mode switch
    {
        CopyMode.FullCopy => "普通复制",
        CopyMode.IncrementalCopy => "增量复制",
        CopyMode.OneWaySync => "单向同步",
        CopyMode.AccumulateSync => "累加同步",
        CopyMode.MirrorSync => "镜像同步",
        CopyMode.MoveSync => "移动同步",
        CopyMode.UpdateSync => "更新同步",
        CopyMode.TwoWaySync => "双向同步",
        CopyMode.VerifyOnly => "仅校验",
        _ => Task.Request.Mode.ToString(),
    };

    public string Source => Task.Request.SourceDirectory;

    public string Target => Task.Request.TargetDirectory;

    public string UpdatedAt => Task.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Group => ExecutionOptions.Group;
    public int Priority => ExecutionOptions.Priority;
    public string Dependencies => ExecutionOptions.EffectiveDependencies.Count == 0
        ? "无"
        : $"{ExecutionOptions.EffectiveDependencies.Count} 项";
    public string LastRunAt => latestRun is null ? "尚未运行" : latestRun.CompletedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string LastResult => latestRun?.Status ?? "—";
}
