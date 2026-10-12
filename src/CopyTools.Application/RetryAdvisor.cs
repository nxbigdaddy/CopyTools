namespace CopyTools.Application;

public sealed record RetryAdvice(int MaxAttempts, TimeSpan Delay, TimeSpan? AvailabilityWait, string Reason);

/// <summary>根据失败类别给出重试与等待策略建议。</summary>
public sealed class RetryAdvisor
{
    public RetryAdvice Advise(string errorMessage)
    {
        return FailureCategorizer.Categorize(errorMessage) switch
        {
            "网络/连接失败" => new RetryAdvice(5, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2),
                "网络不稳定，建议多次重试并等待设备可用。"),
            "文件被占用" => new RetryAdvice(3, TimeSpan.FromSeconds(3), null,
                "文件被占用，稍后重试。"),
            "权限不足" => new RetryAdvice(1, TimeSpan.Zero, null,
                "权限问题重试无效，建议先修复权限。"),
            "磁盘空间不足" => new RetryAdvice(1, TimeSpan.Zero, null,
                "空间不足重试无效，请清理空间或更换目标。"),
            "校验失败" => new RetryAdvice(3, TimeSpan.FromSeconds(2), null,
                "校验失败建议重试并检查存储介质。"),
            _ => new RetryAdvice(3, TimeSpan.FromSeconds(1), null, "使用默认重试策略。"),
        };
    }
}
