using System.Text.RegularExpressions;

namespace CopyTools.Application;

public sealed record SimilarFileGroup(string Reason, IReadOnlyList<string> Paths);

/// <summary>按规范化名称与大小+类型识别"相似文件"（在精确去重之外的启发式）。</summary>
public sealed class SimilarFileDetector
{
    public IReadOnlyList<SimilarFileGroup> Detect(IReadOnlyList<string> paths)
    {
        var byName = paths
            .GroupBy(NormalizeName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new SimilarFileGroup("相似名称", group.ToArray()));

        var bySize = paths
            .GroupBy(path => $"{new FileInfo(path).Length}|{Path.GetExtension(path)}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new SimilarFileGroup("相同大小与类型", group.ToArray()));

        return byName.Concat(bySize).ToArray();
    }

    private static string NormalizeName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        name = Regex.Replace(name, @"\s*\(\d+\)$", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s*-\s*(copy|副本)$", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"_副本$", string.Empty, RegexOptions.IgnoreCase);
        return $"{name.ToLowerInvariant()}{extension.ToLowerInvariant()}";
    }
}
