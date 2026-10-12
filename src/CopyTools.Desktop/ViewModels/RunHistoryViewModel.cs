using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed record RunHistoryViewModel(RunHistoryEntry Run)
{
    public string TaskName => Run.TaskName;

    public string CompletedAt => Run.CompletedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string Status => Run.Status;

    public string Summary
    {
        get
        {
            var verified = Run.Items.Count(item => item.Action == CopyAction.Verify && item.Result == "成功");
            var verificationFailures = Run.Items.Count(item => item.Action == CopyAction.Verify && item.Result == "失败");
            return $"成功 {Run.SuccessfulFiles} · 跳过 {Run.SkippedFiles} · 失败 {Run.FailedFiles}"
                + (verified + verificationFailures == 0 ? string.Empty
                    : $" · 校验成功 {verified} / 失败 {verificationFailures}");
        }
    }

    public IReadOnlyList<RunHistoryItemViewModel> Items =>
        Run.Items.Select(item => new RunHistoryItemViewModel(item)).ToArray();
}

public sealed record RunHistoryItemViewModel(RunHistoryItem Item)
{
    public string RelativePath => Item.RelativePath;

    public string Action => Item.Action.ToString();

    public string Result => Item.Result;

    public int Attempts => Item.Attempts;

    public string Error => Item.ErrorMessage ?? string.Empty;
}
