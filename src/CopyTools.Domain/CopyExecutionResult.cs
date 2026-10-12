namespace CopyTools.Domain;

public sealed record CopyExecutionResult(
    int SuccessfulFiles,
    int SkippedFiles,
    long CopiedBytes,
    IReadOnlyList<CopyItemFailure> Failures,
    bool WasCancelled,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<CopyItemExecutionResult>? Items = null,
    IReadOnlyList<QuarantinedFileResult>? QuarantinedFiles = null)
{
    public IReadOnlyList<CopyItemExecutionResult> EffectiveItems => Items ?? [];

    public IReadOnlyList<QuarantinedFileResult> EffectiveQuarantinedFiles => QuarantinedFiles ?? [];

    public bool IsSuccessful => !WasCancelled && Failures.Count == 0;

    public string Status => WasCancelled
        ? "已取消"
        : Failures.Count > 0
            ? SuccessfulFiles > 0 ? "部分成功" : "失败"
            : "成功";
}
