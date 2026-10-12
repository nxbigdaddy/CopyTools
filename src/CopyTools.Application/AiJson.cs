using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopyTools.Application;

/// <summary>从 AI 返回文本中稳健地提取 JSON（容忍 Markdown 代码块与前后缀）。</summary>
public static class AiJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Parse<T>(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        var json = ExtractJsonObject(text);
        if (json.Length == 0)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = trimmed.IndexOf('\n', fenceStart);
            if (contentStart < 0)
            {
                contentStart = fenceStart + 3;
            }
            else
            {
                contentStart++;
            }

            var fenceEnd = trimmed.IndexOf("```", contentStart, StringComparison.Ordinal);
            trimmed = fenceEnd > contentStart ? trimmed[contentStart..fenceEnd] : trimmed[contentStart..];
        }

        var start = trimmed.IndexOfAny(['{', '[']);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = trimmed.LastIndexOfAny(['}', ']']);
        return end <= start ? string.Empty : trimmed[start..(end + 1)];
    }
}
