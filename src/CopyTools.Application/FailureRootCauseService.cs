using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record FailureAnalysis(IReadOnlyList<FailureCause> Causes, string Advice);

/// <summary>聚合失败并生成根因建议，默认按类别模板，可选 LLM 润色。</summary>
public sealed class FailureRootCauseService
{
    private const string SystemPrompt =
        "你是文件同步工具的故障诊断助手。根据失败类别与样本路径，用中文给出可执行的一条修复建议，不要罗列过程。";

    private static readonly IReadOnlyDictionary<string, string> AdviceByCategory =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["权限不足"] = "多次因权限失败：检查目标目录 ACL，或改用映射驱动器并重新保存凭据。",
            ["磁盘空间不足"] = "目标磁盘空间不足：清理空间、扩大保留策略或更换目标位置。",
            ["文件被占用"] = "目标文件被占用：关闭占用程序，或启用 VSS 一致性快照。",
            ["路径不存在"] = "源或目标路径不存在：检查设备是否连接、网络共享是否可达。",
            ["网络/连接失败"] = "网络连接不稳定：启用设备可用性等待与自动重试，检查网络与代理设置。",
            ["校验失败"] = "内容校验失败：可能磁盘损坏或传输不稳定，建议重新执行并启用严格校验。",
        };

    public async Task<FailureAnalysis> AnalyzeAsync(
        IReadOnlyList<RunHistoryEntry> runs,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        var causes = new FailureAggregationService().Aggregate(runs, top: 5);
        var advice = BuildAdvice(causes);
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var text = await assistant.CompleteAsync(SystemPrompt, BuildSummary(causes), cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    advice = text;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return new FailureAnalysis(causes, advice);
    }

    private static string BuildAdvice(IReadOnlyList<FailureCause> causes)
    {
        if (causes.Count == 0)
        {
            return "未发现失败项。";
        }

        return AdviceByCategory.TryGetValue(causes[0].Category, out var advice)
            ? advice
            : $"主要失败类别为「{causes[0].Category}」，请查看运行历史获取详情。";
    }

    private static string BuildSummary(IReadOnlyList<FailureCause> causes) =>
        "失败类别：" + string.Join("；", causes.Select(cause => $"{cause.Category}×{cause.Count}"));
}
