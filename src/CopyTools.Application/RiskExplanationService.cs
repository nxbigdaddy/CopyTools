using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record RiskExplanation(
    int TotalFiles,
    int NewFiles,
    int OverwriteFiles,
    int QuarantineFiles,
    int RemoveSourceFiles,
    int ConflictCount,
    long BytesToCopy,
    bool Blocked,
    string RiskLevel,
    string Narrative);

/// <summary>把复制计划翻译成自然语言风险解释，默认模板，可选 LLM 润色。</summary>
public sealed class RiskExplanationService
{
    private const string SystemPrompt =
        "你是文件同步工具的风险解释助手。用一段简洁的中文总结计划将执行的变更与风险，"
        + "强调删除/覆盖是否可逆（删除进入隔离区、覆盖可保留旧版本），不要给出执行指令。";

    public async Task<RiskExplanation> ExplainAsync(
        CopyPlan plan,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        var facts = Build(plan);
        var narrative = BuildTemplate(facts);
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var text = await assistant.CompleteAsync(SystemPrompt, BuildTemplate(facts), cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    narrative = text;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return facts with { Narrative = narrative };
    }

    private static RiskExplanation Build(CopyPlan plan)
    {
        var destructive = plan.FilesToQuarantine + plan.SourceFilesToRemove + plan.Conflicts > 0;
        var risk = destructive ? "高" : plan.FilesToOverwrite > 0 ? "中" : "低";
        return new RiskExplanation(
            plan.TotalFiles,
            plan.NewFiles,
            plan.FilesToOverwrite,
            plan.FilesToQuarantine,
            plan.SourceFilesToRemove,
            plan.Conflicts,
            plan.BytesToCopy,
            plan.UnresolvedConflicts > 0,
            risk,
            string.Empty);
    }

    private static string BuildTemplate(RiskExplanation facts) =>
        $"本次共 {facts.TotalFiles:N0} 个文件：新增 {facts.NewFiles:N0}、覆盖 {facts.OverwriteFiles:N0}、"
        + $"隔离 {facts.QuarantineFiles:N0}、清理源 {facts.RemoveSourceFiles:N0}、冲突 {facts.ConflictCount:N0}，"
        + $"预计 {facts.BytesToCopy:N0} 字节。风险等级：{facts.RiskLevel}。"
        + (facts.QuarantineFiles + facts.RemoveSourceFiles > 0
            ? "删除类操作会先移入隔离区，可随时恢复。"
            : "本次不涉及删除类操作。");
}
