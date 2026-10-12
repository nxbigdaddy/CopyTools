namespace CopyTools.Application;

public sealed record StorageSample(DateTime TimestampUtc, long TotalBytes);

public sealed record StorageTrendPrediction(
    double AverageBytesPerDay,
    long ProjectedBytesInDays,
    int DaysUntilFull,
    bool InsufficientData);

/// <summary>基于历史容量样本线性外推存储增长，预测磁盘耗尽时间。</summary>
public sealed class StorageTrendService
{
    public StorageTrendPrediction Predict(
        IReadOnlyList<StorageSample> samples,
        long availableBytes,
        int horizonDays = 30)
    {
        if (samples.Count < 2 || availableBytes <= 0)
        {
            return new StorageTrendPrediction(0, 0, -1, true);
        }

        var ordered = samples.OrderBy(sample => sample.TimestampUtc).ToArray();
        var first = ordered[0];
        var last = ordered[^1];
        var elapsedDays = Math.Max(1, (last.TimestampUtc - first.TimestampUtc).TotalDays);
        var growth = Math.Max(0, last.TotalBytes - first.TotalBytes);
        var averagePerDay = growth / elapsedDays;
        var projected = averagePerDay * Math.Max(1, horizonDays);
        var daysUntilFull = averagePerDay <= 0
            ? -1
            : (int)(availableBytes / averagePerDay);
        return new StorageTrendPrediction(averagePerDay, (long)projected, daysUntilFull, false);
    }
}
