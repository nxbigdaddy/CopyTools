using System.Text.RegularExpressions;
using CopyTools.Domain;

namespace CopyTools.Application;

public static partial class PathTemplateExpander
{
    private static readonly HashSet<string> AllowedEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "USERPROFILE", "TEMP", "TMP", "PUBLIC", "ProgramData", "OneDrive",
    };

    public static CopyTaskRequest Expand(
        CopyTaskRequest request,
        string taskName,
        DateTime? nowLocal = null)
    {
        var now = nowLocal ?? DateTime.Now;
        return request with
        {
            SourceDirectory = ExpandPath(request.SourceDirectory, taskName, now),
            TargetDirectory = ExpandPath(request.TargetDirectory, taskName, now),
            SourcePaths = request.EffectiveSourcePaths
                .Select(path => ExpandPath(path, taskName, now)).ToArray(),
        };
    }

    public static string ExpandPath(string template, string taskName, DateTime nowLocal)
    {
        var expanded = EnvironmentPattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!AllowedEnvironmentVariables.Contains(name))
            {
                throw new CopyTaskValidationException($"不允许的路径环境变量：{name}。");
            }

            return Environment.GetEnvironmentVariable(name)
                ?? throw new CopyTaskValidationException($"路径环境变量未定义：{name}。");
        });
        expanded = DatePattern().Replace(expanded, match =>
        {
            var format = match.Groups[1].Value;
            try { return nowLocal.ToString(format, System.Globalization.CultureInfo.InvariantCulture); }
            catch (FormatException) { throw new CopyTaskValidationException($"无效日期路径格式：{format}。"); }
        });
        expanded = expanded.Replace("{task}", SanitizeSegment(taskName), StringComparison.OrdinalIgnoreCase);
        if (expanded.Contains('{') || expanded.Contains('}'))
        {
            throw new CopyTaskValidationException("路径包含无法识别的模板变量。");
        }

        return expanded;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrEmpty(sanitized) ? "task" : sanitized;
    }

    [GeneratedRegex(@"\$\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentPattern();

    [GeneratedRegex(@"\{date:([^{}]+)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
}
