namespace CopyTools.Application;

public sealed record SpaceAdvice(long AvailableBytes, StorageTrendPrediction Trend, string Narrative);

/// <summary>基于容量趋势给出磁盘耗尽自然语言预警。</summary>
public sealed class SpaceAdvisor
{
    private const string SystemPrompt = "你是存储空间管理助手。根据容量增长趋势，用中文给出一句话预警与建议。";

    public async Task<SpaceAdvice> AdviseAsync(
        long availableBytes,
        IReadOnlyList<StorageSample> samples,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        var trend = new StorageTrendService().Predict(samples, availableBytes);
        var narrative = BuildTemplate(availableBytes, trend);
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var text = await assistant.CompleteAsync(SystemPrompt, BuildTemplate(availableBytes, trend), cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    narrative = text;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return new SpaceAdvice(availableBytes, trend, narrative);
    }

    private static string BuildTemplate(long availableBytes, StorageTrendPrediction trend)
    {
        if (trend.InsufficientData)
        {
            return "历史容量样本不足，暂无法预测磁盘耗尽时间。";
        }

        if (trend.DaysUntilFull < 0)
        {
            return "当前容量未增长，无需担心磁盘耗尽。";
        }

        var gbPerDay = trend.AverageBytesPerDay / (1024d * 1024 * 1024);
        return $"当前可用 {availableBytes / (1024d * 1024 * 1024):0.#} GB，按约 {gbPerDay:0.##} GB/天的增速，"
            + $"预计 {trend.DaysUntilFull} 天后耗尽，建议清理或扩容。";
    }
}
