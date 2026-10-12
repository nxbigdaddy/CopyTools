namespace CopyTools.Application;

public sealed record AnomalyReport(bool IsAnomalous, double Score, string Reason);

/// <summary>对单位时间变更计数做 z-score 异常检测，识别突增。</summary>
public sealed class AnomalyDetector
{
    public AnomalyReport Detect(IReadOnlyList<double> perIntervalCounts, double zScoreThreshold = 3.0)
    {
        if (perIntervalCounts.Count < 5)
        {
            return new AnomalyReport(false, 0, "样本不足");
        }

        var baseline = perIntervalCounts.Take(perIntervalCounts.Count - 1).ToArray();
        var latest = perIntervalCounts[^1];
        var mean = baseline.Average();
        var std = Math.Sqrt(baseline.Average(value => (value - mean) * (value - mean)));
        var effectiveStd = std <= 0 ? 1.0 : std;
        var z = (latest - mean) / effectiveStd;
        return new AnomalyReport(
            z >= zScoreThreshold,
            z,
            z >= zScoreThreshold
                ? $"当前变更率 {latest:0.#} 显著高于基线均值 {mean:0.#}（z={z:0.#}）"
                : "变更率正常");
    }
}
