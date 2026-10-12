using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class PathSafetyValidator
{
    public ValidatedPaths Validate(CopyTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            throw new CopyTaskValidationException("请选择源目录。");
        }

        if (string.IsNullOrWhiteSpace(request.TargetDirectory))
        {
            throw new CopyTaskValidationException("请选择目标目录。");
        }

        string sourcePath;
        string targetPath;
        try
        {
            sourcePath = NormalizeDirectory(request.SourceDirectory);
            targetPath = NormalizeDirectory(request.TargetDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CopyTaskValidationException($"目录路径无效：{exception.Message}");
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new CopyTaskValidationException("源目录不存在或当前无法访问。");
        }

        if (PathEquals(sourcePath, targetPath))
        {
            throw new CopyTaskValidationException("源目录和目标目录不能相同。");
        }

        if (IsDescendant(targetPath, sourcePath))
        {
            throw new CopyTaskValidationException("目标目录不能位于源目录内部，否则会形成递归复制。");
        }

        if (IsDescendant(sourcePath, targetPath))
        {
            throw new CopyTaskValidationException("源目录不能位于目标目录内部。");
        }

        ValidateNotCriticalTarget(targetPath);

        return new ValidatedPaths(sourcePath, targetPath);
    }

    public string ValidateTargetDirectory(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new CopyTaskValidationException("请选择目标目录。");
        try
        {
            var target = NormalizeDirectory(targetDirectory);
            ValidateNotCriticalTarget(target);
            return target;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CopyTaskValidationException($"目标目录路径无效：{exception.Message}");
        }
    }

    private static string NormalizeDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(fullPath);

        if (root is not null && PathEquals(fullPath, root))
        {
            return root;
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static bool IsDescendant(string candidate, string parent)
    {
        var relativePath = Path.GetRelativePath(parent, candidate);
        return relativePath != "."
            && !Path.IsPathRooted(relativePath)
            && relativePath != ".."
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathEquals(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            StringComparison.OrdinalIgnoreCase);

    private static void ValidateNotCriticalTarget(string targetPath)
    {
        var critical = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(commonData))
        {
            critical.Add(commonData);
        }

        if (critical.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path =>
                PathEquals(targetPath, path)
                || IsDescendant(targetPath, path)))
        {
            throw new CopyTaskValidationException(
                "目标目录位于 Windows 或程序关键目录中。为防止系统损坏，请选择普通数据目录。");
        }
    }
}

public sealed record ValidatedPaths(string SourceDirectory, string TargetDirectory);
