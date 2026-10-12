namespace CopyTools.Application;

public sealed record HealthScoreReport(int Score, string Grade, IReadOnlyList<string> Concerns);

/// <summary>综合校验成功率、备份新鲜度、隔离区堆积与失败率，给出整体健康评分。</summary>
public sealed class HealthScoreService
{
    public HealthScoreReport Evaluate(
        double verificationSuccessRate,
        DateTime? lastSuccessfulBackupUtc,
        int quarantineBacklog,
        double recentFailureRate)
    {
        var score = 100;
        var concerns = new List<string>();

        if (verificationSuccessRate < 100)
        {
            score -= (int)Math.Round((100 - verificationSuccessRate) * 0.4);
            concerns.Add("存在校验失败项");
        }

        if (recentFailureRate > 0)
        {
            score -= (int)Math.Round(recentFailureRate * 100 * 0.3);
            concerns.Add("最近运行存在失败");
        }

        if (quarantineBacklog > 100)
        {
            score -= 10;
            concerns.Add("隔离区堆积较多");
        }

        if (lastSuccessfulBackupUtc is null || DateTime.UtcNow - lastSuccessfulBackupUtc > TimeSpan.FromDays(7))
        {
            score -= 15;
            concerns.Add("备份不新鲜");
        }

        score = Math.Clamp(score, 0, 100);
        var grade = score switch
        {
            >= 90 => "优秀",
            >= 75 => "良好",
            >= 60 => "一般",
            >= 40 => "警告",
            _ => "危险",
        };
        return new HealthScoreReport(score, grade, concerns);
    }
}
