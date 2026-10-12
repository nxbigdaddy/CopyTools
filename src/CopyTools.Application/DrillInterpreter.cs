using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record DrillNarrative(RestoreDrillResult Result, string Narrative);

/// <summary>把恢复演练结果翻译成可读的"备份可恢复性"结论。</summary>
public sealed class DrillInterpreter
{
    private const string SystemPrompt = "你是备份恢复助手。根据恢复演练结果，用中文一句话说明备份是否可恢复及原因。";

    public async Task<DrillNarrative> InterpretAsync(
        RestoreDrillResult result,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        var narrative = BuildTemplate(result);
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var text = await assistant.CompleteAsync(SystemPrompt, BuildTemplate(result), cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    narrative = text;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return new DrillNarrative(result, narrative);
    }

    private static string BuildTemplate(RestoreDrillResult result) =>
        result.IsSuccessful
            ? $"恢复演练通过：{result.VerifiedFiles} 个文件全部校验成功，备份可恢复。"
            : $"恢复演练未完全通过：{result.FailedRelativePaths.Count} 个文件校验失败"
                + $"（{string.Join("、", result.FailedRelativePaths.Take(5))}）。";
}
