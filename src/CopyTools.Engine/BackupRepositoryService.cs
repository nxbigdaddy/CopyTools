using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class BackupRepositoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DirectoryScanner _scanner;
    private readonly SafeFileCopier _copier;

    public BackupRepositoryService(DirectoryScanner? scanner = null, SafeFileCopier? copier = null)
    {
        _scanner = scanner ?? new DirectoryScanner();
        _copier = copier ?? new SafeFileCopier();
    }

    public async Task<BackupResult> CreateBackupAsync(
        BackupTaskRequest request,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(request.SourceDirectory);
        var repository = Path.GetFullPath(request.RepositoryDirectory);
        if (!Directory.Exists(source))
        {
            throw new CopyTaskValidationException("备份源目录不存在。");
        }

        if (PathEquals(source, repository)
            || IsDescendant(repository, source)
            || IsDescendant(source, repository))
        {
            throw new CopyTaskValidationException("备份源目录和版本库不能相同或互相包含。");
        }

        Directory.CreateDirectory(repository);
        var objectsRoot = Path.Combine(repository, "objects");
        var snapshotsRoot = Path.Combine(repository, "snapshots");
        Directory.CreateDirectory(objectsRoot);
        Directory.CreateDirectory(snapshotsRoot);
        var files = await _scanner.ScanAsync(source, request.Filters, cancellationToken);
        var records = new List<BackupFileRecord>(files.Count);
        var newObjects = 0;
        var reusedObjects = 0;
        long writtenBytes = 0;
            long completedBytes = 0;
        var totalBytes = files.Sum(file => file.Length);

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var hash = await HashAsync(file.FullPath, cancellationToken);
            var objectRelativePath = request.Mode == BackupMode.IncrementalHistory
                ? Path.Combine("objects", hash[..2], hash)
                : Path.Combine("objects", Guid.NewGuid().ToString("N"));
            var objectPath = Path.Combine(repository, objectRelativePath);
            if (!File.Exists(objectPath))
            {
                long currentFileBytes = 0;
                var byteProgress = new InlineProgress<long>(value =>
                {
                    currentFileBytes += value;
                    progress?.Report(new CopyProgress(
                        index, files.Count, completedBytes + currentFileBytes, totalBytes,
                        file.RelativePath, "正在写入备份对象", currentFileBytes, file.Length));
                });
                await _copier.CopyAsync(
                    file.FullPath,
                    objectPath,
                    file.LastWriteTimeUtc,
                    FileAttributes.Archive,
                    false,
                    request.VerificationMode,
                    byteProgress,
                    cancellationToken);
                newObjects++;
                writtenBytes += file.Length;
            }
            else
            {
                reusedObjects++;
            }

            completedBytes += file.Length;
            records.Add(new BackupFileRecord(
                file.RelativePath,
                file.Length,
                file.LastWriteTimeUtc,
                file.Attributes,
                hash,
                objectRelativePath));
        }

        var manifestSource = Path.GetFullPath(request.ManifestSourceDirectory ?? source);
        var snapshot = new BackupSnapshot(Guid.NewGuid(), DateTime.UtcNow, manifestSource, request.Mode, records);
        var manifestPath = Path.Combine(
            snapshotsRoot,
            $"{snapshot.CreatedAtUtc:yyyyMMdd-HHmmss-fffffff}-{snapshot.Id:N}.json");
        await WriteJsonAtomicAsync(manifestPath, snapshot, cancellationToken);
        return new BackupResult(snapshot, newObjects, reusedObjects, writtenBytes);
    }

    public async Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(
        string repositoryDirectory,
        CancellationToken cancellationToken = default)
    {
        var snapshotsRoot = Path.Combine(Path.GetFullPath(repositoryDirectory), "snapshots");
        if (!Directory.Exists(snapshotsRoot))
        {
            return [];
        }

        var snapshots = new List<BackupSnapshot>();
        foreach (var path in Directory.EnumerateFiles(snapshotsRoot, "*.json").OrderByDescending(path => path))
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<BackupSnapshot>(stream, JsonOptions, cancellationToken);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    public async Task RestoreAsync(
        string repositoryDirectory,
        BackupSnapshot snapshot,
        string restoreDirectory,
        IReadOnlyCollection<string>? selectedRelativePaths = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var repository = Path.GetFullPath(repositoryDirectory);
        var restoreRoot = Path.GetFullPath(restoreDirectory);
        var selected = selectedRelativePaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in snapshot.Files.Where(file => selected is null || selected.Contains(file.RelativePath)))
        {
            var objectPath = Path.Combine(repository, file.ObjectRelativePath);
            if (!File.Exists(objectPath))
            {
                throw new InvalidDataException($"备份对象缺失：{file.ObjectRelativePath}。");
            }

            var targetPath = Path.Combine(restoreRoot, file.RelativePath);
            if (File.Exists(targetPath) && !overwrite)
            {
                throw new IOException($"恢复目标已存在：{file.RelativePath}。");
            }

            await _copier.CopyAsync(
                objectPath,
                targetPath,
                file.LastWriteTimeUtc,
                file.Attributes,
                overwrite,
                VerificationMode.Sha256,
                null,
                cancellationToken);
        }
    }

    public async Task<int> ApplyRetentionAsync(
        string repositoryDirectory,
        int keepLatest,
        TimeSpan? keepFor = null,
        CancellationToken cancellationToken = default)
    {
        keepLatest = Math.Max(1, keepLatest);
        var repository = Path.GetFullPath(repositoryDirectory);
        var snapshots = await GetSnapshotsAsync(repository, cancellationToken);
        var cutoff = keepFor is null ? DateTime.MinValue : DateTime.UtcNow - keepFor.Value;
        var toDelete = snapshots
            .Skip(keepLatest)
            .Where(snapshot => keepFor is null || snapshot.CreatedAtUtc < cutoff)
            .Where(IsMutableNow)
            .ToArray();
        return await DeleteSnapshotsAsync(repository, toDelete, cancellationToken);
    }

    public async Task<int> ApplyGfsRetentionAsync(
        string repositoryDirectory,
        BackupRetentionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var repository = Path.GetFullPath(repositoryDirectory);
        var snapshots = await GetSnapshotsAsync(repository, cancellationToken);
        var retained = BackupRetentionPlanner.SelectRetained(snapshots, policy);
        var toDelete = snapshots
            .Where(snapshot => !retained.Contains(snapshot.Id))
            .Where(IsMutableNow)
            .ToArray();
        return await DeleteSnapshotsAsync(repository, toDelete, cancellationToken);
    }

    public async Task<BackupIntegrityReport> VerifyIntegrityAsync(
        string repositoryDirectory,
        CancellationToken cancellationToken = default)
    {
        var repository = Path.GetFullPath(repositoryDirectory);
        var snapshots = await GetSnapshotsAsync(repository, cancellationToken);
        var issues = new List<BackupIntegrityIssue>();
        var checkedObjects = 0;
        foreach (var snapshot in snapshots)
        {
            foreach (var file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedObjects++;
                var objectPath = Path.Combine(repository, file.ObjectRelativePath);
                if (!File.Exists(objectPath))
                {
                    issues.Add(new BackupIntegrityIssue(
                        snapshot.Id, file.RelativePath, file.ObjectRelativePath,
                        BackupIntegrityIssueKind.Missing, file.Sha256));
                    continue;
                }

                var actual = await HashAsync(objectPath, cancellationToken);
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new BackupIntegrityIssue(
                        snapshot.Id, file.RelativePath, file.ObjectRelativePath,
                        BackupIntegrityIssueKind.Corrupted, file.Sha256));
                }
            }
        }

        return new BackupIntegrityReport(
            checkedObjects,
            issues.Count(issue => issue.Kind == BackupIntegrityIssueKind.Missing),
            issues.Count(issue => issue.Kind == BackupIntegrityIssueKind.Corrupted),
            issues);
    }

    public async Task<int> RepairAsync(
        string repositoryDirectory,
        BackupIntegrityReport report,
        CancellationToken cancellationToken = default)
    {
        var repository = Path.GetFullPath(repositoryDirectory);
        var snapshots = await GetSnapshotsAsync(repository, cancellationToken);
        var byId = snapshots.ToDictionary(snapshot => snapshot.Id);
        var repaired = 0;
        foreach (var issue in report.Issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(issue.SnapshotId, out var snapshot))
            {
                continue;
            }

            var sourcePath = Path.Combine(snapshot.SourceDirectory, issue.RelativePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var sourceHash = await HashAsync(sourcePath, cancellationToken);
            if (!string.Equals(sourceHash, issue.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var objectPath = Path.Combine(repository, issue.ObjectRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            ClearReadOnly(objectPath);
            await _copier.CopyAsync(
                sourcePath,
                objectPath,
                File.GetLastWriteTimeUtc(sourcePath),
                FileAttributes.Archive,
                true,
                VerificationMode.Sha256,
                null,
                cancellationToken);
            repaired++;
        }

        return repaired;
    }

    public async Task MarkImmutableAsync(
        string repositoryDirectory,
        Guid snapshotId,
        DateTime untilUtc,
        CancellationToken cancellationToken = default)
    {
        var repository = Path.GetFullPath(repositoryDirectory);
        var manifest = Directory
            .EnumerateFiles(Path.Combine(repository, "snapshots"), $"*{snapshotId:N}.json")
            .SingleOrDefault() ?? throw new InvalidOperationException("指定的快照不存在。");
        var snapshot = await ReadSnapshotAsync(manifest, cancellationToken)
            ?? throw new InvalidOperationException("无法读取快照清单。");
        await WriteJsonAtomicAsync(manifest, snapshot with { ImmutableUntilUtc = untilUtc }, cancellationToken);
        foreach (var file in snapshot.Files)
        {
            var objectPath = Path.Combine(repository, file.ObjectRelativePath);
            try
            {
                if (File.Exists(objectPath))
                {
                    File.SetAttributes(objectPath, File.GetAttributes(objectPath) | FileAttributes.ReadOnly);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public async Task<RestoreDrillResult> DrillRestoreAsync(
        string repositoryDirectory,
        BackupSnapshot snapshot,
        string drillDirectory,
        CancellationToken cancellationToken = default)
    {
        var drillRoot = Path.GetFullPath(drillDirectory);
        Directory.CreateDirectory(drillRoot);
        await RestoreAsync(repositoryDirectory, snapshot, drillRoot, overwrite: true, cancellationToken: cancellationToken);
        var failed = new List<string>();
        var verified = 0;
        foreach (var file in snapshot.Files)
        {
            var path = Path.Combine(drillRoot, file.RelativePath);
            if (!File.Exists(path))
            {
                failed.Add(file.RelativePath);
                continue;
            }

            var actual = await HashAsync(path, cancellationToken);
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                failed.Add(file.RelativePath);
            }
            else
            {
                verified++;
            }
        }

        return new RestoreDrillResult(snapshot.Id, verified, failed);
    }

    private static bool IsMutableNow(BackupSnapshot snapshot) =>
        snapshot.ImmutableUntilUtc is null || snapshot.ImmutableUntilUtc <= DateTime.UtcNow;

    private async Task<int> DeleteSnapshotsAsync(
        string repository,
        IReadOnlyCollection<BackupSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in snapshots)
        {
            var manifest = Directory.EnumerateFiles(Path.Combine(repository, "snapshots"), $"*{snapshot.Id:N}.json")
                .SingleOrDefault();
            if (manifest is not null)
            {
                File.Delete(manifest);
            }
        }

        var referenced = (await GetSnapshotsAsync(repository, cancellationToken))
            .SelectMany(snapshot => snapshot.Files)
            .Select(file => Path.GetFullPath(Path.Combine(repository, file.ObjectRelativePath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var objectsRoot = Path.Combine(repository, "objects");
        if (Directory.Exists(objectsRoot))
        {
            foreach (var objectPath in Directory.EnumerateFiles(objectsRoot, "*", SearchOption.AllDirectories))
            {
                if (!referenced.Contains(Path.GetFullPath(objectPath)))
                {
                    ClearReadOnly(objectPath);
                    File.Delete(objectPath);
                }
            }
        }

        return snapshots.Count;
    }

    private async Task<BackupSnapshot?> ReadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BackupSnapshot>(stream, JsonOptions, cancellationToken);
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendant(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != "." && !Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
