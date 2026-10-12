using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class IncompleteExecutionViewModel(IncompleteExecution execution, string taskName)
{
    public IncompleteExecution Execution { get; } = execution;
    public string TaskName { get; } = taskName;
    public string StartedAt => Execution.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public int CompletedItems => Execution.CompletedItems.Count;
    public string Source => Execution.Request.SourceDirectory;
    public string Target => Execution.Request.TargetDirectory;
}
