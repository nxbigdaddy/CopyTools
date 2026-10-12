namespace CopyTools.Application;

public sealed record HealthReport(HealthScoreReport Score, DashboardSummary Summary, string Narrative);

/// <summary>把健康评分与仪表盘指标合成为自然语言报告。</summary>
public sealed class HealthReportService
{
    private const string SystemPrompt = "你是运维报告助手。根据健康评分与运行统计，用中文写一段简洁的月度状态摘要。";

    public async Task<HealthReport> BuildAsync(
        HealthScoreReport score,
        DashboardSummary summary,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        var narrative = BuildTemplate(score, summary);
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var text = await assistant.CompleteAsync(SystemPrompt, BuildTemplate(score, summary), cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    narrative = text;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return new HealthReport(score, summary, narrative);
    }

    private static string BuildTemplate(HealthScoreReport score, DashboardSummary summary) =>
        $"健康评分 {score.Score}（{score.Grade}）。近 {summary.TotalRuns} 次运行成功率 {summary.SuccessRate:0.#}%，"
        + $"共复制 {summary.TotalCopiedBytes:N0} 字节，隔离区 {summary.QuarantinedFiles} 项。"
        + (score.Concerns.Count == 0 ? "未发现隐患。" : "隐患：" + string.Join("；", score.Concerns) + "。");
}
