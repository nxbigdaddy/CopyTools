namespace CopyTools.Domain;

public enum RunConditionKind
{
    None,
    MinimumDiskSpaceGb,
    FileExists,
    PreviousRunSucceeded,
}

/// <summary>计划/自动任务执行前的门控条件。</summary>
public sealed record RunCondition(
    RunConditionKind Kind,
    string? Argument = null);
