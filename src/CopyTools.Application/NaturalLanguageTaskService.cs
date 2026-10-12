using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record AiTaskDraft(
    string? SourceDirectory = null,
    string? TargetDirectory = null,
    CopyMode Mode = CopyMode.AccumulateSync,
    double Confidence = 0,
    IReadOnlyList<string>? ExcludePatterns = null);

/// <summary>把自然语言描述解析为任务草稿（源/目标/模式/排除项），支持 LLM 与规则双路径。</summary>
public sealed class NaturalLanguageTaskService
{
    private const string SystemPrompt =
        "你是文件同步助手。把用户描述解析为 JSON 任务草稿，字段：sourceDirectory、targetDirectory、"
        + "mode、confidence(0-1)、excludePatterns(字符串数组)。mode 取值：FullCopy、IncrementalCopy、"
        + "AccumulateSync、MirrorSync、OneWaySync、TwoWaySync、MoveSync、UpdateSync、VerifyOnly。";

    public async Task<AiTaskDraft> ParseAsync(
        string description,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var draft = await assistant.CompleteJsonAsync<AiTaskDraft>(SystemPrompt, description, cancellationToken);
                if (draft is not null && !string.IsNullOrWhiteSpace(draft.SourceDirectory))
                {
                    return draft with { Confidence = Math.Clamp(draft.Confidence, 0, 1) };
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return ParseByRules(description);
    }

    private static AiTaskDraft ParseByRules(string description)
    {
        var source = MatchPath(description, ["把", "将", "从"]);
        var target = MatchPath(description, ["同步到", "复制到", "备份到", "移动到", "到"]);
        var mode = DetectMode(description);
        var excludes = ExtractExcludes(description);
        return new AiTaskDraft(
            source,
            target,
            mode,
            source is not null && target is not null ? 0.6 : 0.3,
            excludes);
    }

    private static CopyMode DetectMode(string text)
    {
        if (text.Contains("镜像", StringComparison.Ordinal)) return CopyMode.MirrorSync;
        if (text.Contains("双向", StringComparison.Ordinal)) return CopyMode.TwoWaySync;
        if (text.Contains("移动", StringComparison.Ordinal)) return CopyMode.MoveSync;
        if (text.Contains("更新", StringComparison.Ordinal)) return CopyMode.UpdateSync;
        if (text.Contains("单向", StringComparison.Ordinal)) return CopyMode.OneWaySync;
        if (text.Contains("增量", StringComparison.Ordinal)) return CopyMode.IncrementalCopy;
        if (text.Contains("备份", StringComparison.Ordinal)) return CopyMode.AccumulateSync;
        return text.Contains("复制", StringComparison.Ordinal) ? CopyMode.FullCopy : CopyMode.AccumulateSync;
    }

    private static string? MatchPath(string text, string[] markers)
    {
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var rest = text[(index + marker.Length)..].TrimStart();
            if (rest.Length == 0)
            {
                continue;
            }

            if (rest[0] is '"' or '\'' or '“' or '‘')
            {
                var quote = rest[0];
                var close = rest.IndexOf(quote, 1);
                if (close > 1)
                {
                    return rest[1..close];
                }
            }

            var end = rest.IndexOfAny(['，', ',', ' ', '；', '。', '：']);
            return end < 0 ? rest : rest[..end];
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractExcludes(string text)
    {
        var result = new List<string>();
        foreach (var marker in new[] { "排除", "跳过", "忽略" })
        {
            var searchFrom = 0;
            while (searchFrom < text.Length)
            {
                var index = text.IndexOf(marker, searchFrom, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                var rest = text[(index + marker.Length)..].TrimStart();
                var end = rest.IndexOfAny(['，', ',', '。', '；', ' ']);
                var token = (end < 0 ? rest : rest[..end]).Trim();
                if (token.Length > 0 && !result.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(token);
                }

                searchFrom = end < 0 ? text.Length : index + marker.Length + end + 1;
            }
        }

        return result;
    }
}
