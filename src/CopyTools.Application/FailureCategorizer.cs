namespace CopyTools.Application;

/// <summary>把错误消息归类为稳定的失败类别，供聚合与建议复用。</summary>
public static class FailureCategorizer
{
    public static string Categorize(string message)
    {
        if (message.Contains("访问被拒绝", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("权限", StringComparison.OrdinalIgnoreCase))
        {
            return "权限不足";
        }

        if (message.Contains("空间不足", StringComparison.OrdinalIgnoreCase)
            || message.Contains("空间", StringComparison.OrdinalIgnoreCase)
            || message.Contains("space", StringComparison.OrdinalIgnoreCase))
        {
            return "磁盘空间不足";
        }

        if (message.Contains("占用", StringComparison.OrdinalIgnoreCase)
            || message.Contains("being used", StringComparison.OrdinalIgnoreCase))
        {
            return "文件被占用";
        }

        if (message.Contains("不存在", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "路径不存在";
        }

        if (message.Contains("网络", StringComparison.OrdinalIgnoreCase)
            || message.Contains("连接", StringComparison.OrdinalIgnoreCase)
            || message.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "网络/连接失败";
        }

        if (message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)
            || message.Contains("校验", StringComparison.OrdinalIgnoreCase)
            || message.Contains("不一致", StringComparison.OrdinalIgnoreCase))
        {
            return "校验失败";
        }

        return "其他";
    }
}
