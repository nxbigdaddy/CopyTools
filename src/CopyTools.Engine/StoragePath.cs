namespace CopyTools.Engine;

public static class StoragePath
{
    public static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            throw new ArgumentException("存储相对路径不能包含上级目录片段。", nameof(path));
        }

        return normalized;
    }

    public static string Combine(string left, string right)
    {
        var normalizedLeft = NormalizeRelative(left);
        var normalizedRight = NormalizeRelative(right);
        return string.IsNullOrEmpty(normalizedLeft)
            ? normalizedRight
            : string.IsNullOrEmpty(normalizedRight)
                ? normalizedLeft
                : $"{normalizedLeft}/{normalizedRight}";
    }
}
