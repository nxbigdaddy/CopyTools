using System.Text.RegularExpressions;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed record AiFilterDraft(
    IReadOnlyList<string>? IncludePatterns = null,
    IReadOnlyList<string>? ExcludePatterns = null,
    IReadOnlyList<string>? ExcludedDirectories = null,
    long? MinimumBytes = null,
    long? MaximumBytes = null)
{
    public bool HasAny => (IncludePatterns?.Count ?? 0) + (ExcludePatterns?.Count ?? 0)
        + (ExcludedDirectories?.Count ?? 0) > 0
        || MinimumBytes is not null || MaximumBytes is not null;

    public FileFilterOptions ToOptions() =>
        new(IncludePatterns, ExcludePatterns, ExcludedDirectories, MinimumBytes, MaximumBytes);
}

/// <summary>把过滤规则的自然语言描述解析为 FileFilterOptions，支持 LLM 与规则双路径。</summary>
public sealed class NaturalLanguageFilterService
{
    private const string SystemPrompt =
        "你是文件过滤器助手。把用户描述解析为 JSON，字段：includePatterns、excludePatterns、"
        + "excludedDirectories（均为字符串数组）、minimumBytes、maximumBytes（字节数，可空）。";

    public async Task<FileFilterOptions> ParseAsync(
        string description,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var draft = await assistant.CompleteJsonAsync<AiFilterDraft>(SystemPrompt, description, cancellationToken);
                if (draft is { HasAny: true })
                {
                    return draft.ToOptions();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        return ParseByRules(description);
    }

    private static FileFilterOptions ParseByRules(string text)
    {
        var excludes = ExtractList(text, ["排除", "跳过", "忽略"]);
        var directories = ExtractList(text, ["排除目录"]);
        var (minimum, maximum) = ExtractSize(text);
        return new FileFilterOptions(
            ExcludePatterns: excludes.Count == 0 ? null : excludes,
            ExcludedDirectories: directories.Count == 0 ? null : directories,
            MinimumBytes: minimum,
            MaximumBytes: maximum);
    }

    private static IReadOnlyList<string> ExtractList(string text, string[] markers)
    {
        var result = new List<string>();
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var rest = TruncateAt(text[(index + marker.Length)..], ["排除目录", "大于", "小于", "超过", "。", "；"]);
            foreach (var token in rest.Split([' ', '，', ',', '、', '和'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim();
                if (trimmed.Length > 0 && !result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    private static (long? Minimum, long? Maximum) ExtractSize(string text)
    {
        var greater = Regex.Match(text, @"大于\s*(\d+(?:\.\d+)?)\s*(GB|MB|KB|字节|G|M|K)?", RegexOptions.IgnoreCase);
        var less = Regex.Match(text, @"小于\s*(\d+(?:\.\d+)?)\s*(GB|MB|KB|字节|G|M|K)?", RegexOptions.IgnoreCase);
        return (
            greater.Success ? ToBytes(greater.Groups[1].Value, greater.Groups[2].Value) : null,
            less.Success ? ToBytes(less.Groups[1].Value, less.Groups[2].Value) : null);
    }

    private static long ToBytes(string number, string unit)
    {
        var value = double.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
        return (long)(unit.ToUpperInvariant() switch
        {
            "GB" or "G" => value * 1024 * 1024 * 1024,
            "MB" or "M" => value * 1024 * 1024,
            "KB" or "K" => value * 1024,
            _ => value,
        });
    }

    private static string TruncateAt(string text, string[] markers)
    {
        var earliest = text.Length;
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && index < earliest)
            {
                earliest = index;
            }
        }

        return text[..earliest];
    }
}
