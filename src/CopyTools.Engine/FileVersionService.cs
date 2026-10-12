using CopyTools.Domain;
using System.Text.Json;

namespace CopyTools.Engine;

public sealed class FileVersionService
{
    public IReadOnlyList<FileVersionEntry> GetVersions(string versionsDirectory)
    {
        var root = Path.GetFullPath(versionsDirectory);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.version", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Select(file =>
            {
                var metadata = ReadMetadata(file.FullName);
                return new FileVersionEntry(file.FullName, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc,
                    metadata?.RelativePath, metadata?.OriginalTargetPath);
            })
            .ToArray();
    }
    public string? PreserveBeforeOverwrite(
        string targetPath,
        string relativePath,
        VersioningPolicy policy,
        DateTime nowUtc)
    {
        var normalized = policy.Normalize();
        if (!normalized.PreserveOverwrittenFiles || !File.Exists(targetPath)) return null;
        if (normalized.VersionsDirectory is null)
            throw new InvalidOperationException("启用覆盖版本保留时必须配置版本目录。");

        var encodedPath = EncodeRelativePath(relativePath);
        var fileDirectory = Path.Combine(normalized.VersionsDirectory, encodedPath);
        Directory.CreateDirectory(fileDirectory);
        var versionPath = Path.Combine(fileDirectory, $"{nowUtc:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.version");
        File.Copy(targetPath, versionPath, overwrite: false);
        File.SetLastWriteTimeUtc(versionPath, File.GetLastWriteTimeUtc(targetPath));
        File.WriteAllText(versionPath + ".json", JsonSerializer.Serialize(new FileVersionMetadata(
            relativePath, Path.GetFullPath(targetPath), File.GetAttributes(targetPath), nowUtc)));
        ApplyRetention(fileDirectory, normalized, nowUtc);
        return versionPath;
    }

    public void Restore(string versionPath, string targetPath, bool overwrite)
    {
        if (!File.Exists(versionPath)) throw new FileNotFoundException("覆盖版本不存在。", versionPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        File.Copy(versionPath, targetPath, overwrite);
        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(versionPath));
        var metadata = ReadMetadata(versionPath);
        if (metadata is not null) File.SetAttributes(targetPath, metadata.OriginalAttributes);
    }

    private static void ApplyRetention(string directory, VersioningPolicy policy, DateTime nowUtc)
    {
        var cutoff = policy.KeepFor is null ? DateTime.MinValue : nowUtc - policy.KeepFor.Value;
        var files = Directory.EnumerateFiles(directory, "*.version")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc).ThenByDescending(file => file.Name)
            .ToArray();
        foreach (var candidate in files.Select((file, index) => (File: file, Index: index))
                     .Where(candidate => candidate.Index >= policy.KeepLatestPerFile
                         || (policy.KeepFor is not null && candidate.File.CreationTimeUtc < cutoff)))
        {
            var file = candidate.File;
            file.IsReadOnly = false;
            file.Delete();
            var metadataPath = file.FullName + ".json";
            if (File.Exists(metadataPath)) File.Delete(metadataPath);
        }
    }

    private static string EncodeRelativePath(string relativePath)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/'));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static FileVersionMetadata? ReadMetadata(string versionPath)
    {
        var path = versionPath + ".json";
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<FileVersionMetadata>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    private sealed record FileVersionMetadata(
        string RelativePath,
        string OriginalTargetPath,
        FileAttributes OriginalAttributes,
        DateTime PreservedAtUtc);
}

public sealed record FileVersionEntry(
    string VersionPath,
    long Length,
    DateTime CreatedAtUtc,
    DateTime OriginalLastWriteTimeUtc,
    string? RelativePath = null,
    string? OriginalTargetPath = null);
