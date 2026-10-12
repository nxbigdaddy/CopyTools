using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class CopyPlanService
{
    private readonly PathSafetyValidator _pathSafetyValidator;
    private readonly DirectoryScanner _directoryScanner;
    private readonly CopyPlanBuilder _copyPlanBuilder;
    private readonly UpdateSyncPlanBuilder _updateSyncPlanBuilder;
    private readonly OneWaySyncPlanBuilder _oneWaySyncPlanBuilder;
    private readonly TwoWaySyncPlanBuilder _twoWaySyncPlanBuilder;
    private readonly ISyncSnapshotStore? _snapshotStore;
    private readonly DiskSpaceValidator _diskSpaceValidator;

    public CopyPlanService(
        PathSafetyValidator? pathSafetyValidator = null,
        DirectoryScanner? directoryScanner = null,
        CopyPlanBuilder? copyPlanBuilder = null,
        OneWaySyncPlanBuilder? oneWaySyncPlanBuilder = null,
        UpdateSyncPlanBuilder? updateSyncPlanBuilder = null,
        TwoWaySyncPlanBuilder? twoWaySyncPlanBuilder = null,
        ISyncSnapshotStore? snapshotStore = null,
        DiskSpaceValidator? diskSpaceValidator = null)
    {
        _pathSafetyValidator = pathSafetyValidator ?? new PathSafetyValidator();
        _directoryScanner = directoryScanner ?? new DirectoryScanner();
        _copyPlanBuilder = copyPlanBuilder ?? new CopyPlanBuilder();
        _oneWaySyncPlanBuilder = oneWaySyncPlanBuilder ?? new OneWaySyncPlanBuilder();
        _updateSyncPlanBuilder = updateSyncPlanBuilder ?? new UpdateSyncPlanBuilder();
        _twoWaySyncPlanBuilder = twoWaySyncPlanBuilder ?? new TwoWaySyncPlanBuilder();
        _snapshotStore = snapshotStore;
        _diskSpaceValidator = diskSpaceValidator ?? new DiskSpaceValidator();
    }

    public async Task<CopyPlan> CreatePlanAsync(
        CopyTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EffectiveSourcePaths.Count > 1 || request.EffectiveSourcePaths.Any(File.Exists))
            return await CreateMultiSourcePlanAsync(request, cancellationToken);
        var paths = _pathSafetyValidator.Validate(request);
        ValidateVersioning(request, paths.TargetDirectory, [paths.SourceDirectory]);
        var sourceFiles = await _directoryScanner.ScanAsync(
            paths.SourceDirectory,
            request.EffectiveFilters,
            cancellationToken,
            hashMode: ToHashMode(request.VerificationMode));
        var sourceEmptyDirectories = await _directoryScanner.ScanEmptyDirectoriesAsync(
            paths.SourceDirectory,
            request.EffectiveFilters,
            cancellationToken);
        IReadOnlyList<FileEntry> targetFiles = [];
        if (request.Mode is CopyMode.OneWaySync or CopyMode.MirrorSync or CopyMode.UpdateSync or CopyMode.TwoWaySync
            && Directory.Exists(paths.TargetDirectory))
        {
            targetFiles = await _directoryScanner.ScanAsync(
                paths.TargetDirectory,
                request.EffectiveFilters,
                cancellationToken,
                hashMode: ToHashMode(request.VerificationMode));
        }

        IReadOnlyList<SyncFileState> snapshot = [];
        if (request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync)
        {
            if (request.SynchronizationId is null)
            {
                throw new CopyTaskValidationException("更新同步和双向同步需要稳定的同步标识，请先保存任务。");
            }

            if (_snapshotStore is null)
            {
                throw new InvalidOperationException("同步快照存储未配置。");
            }

            snapshot = await _snapshotStore.GetSnapshotAsync(
                request.SynchronizationId.Value,
                cancellationToken);
        }

        var plan = request.Mode switch
        {
            CopyMode.OneWaySync => _oneWaySyncPlanBuilder.Build(
                request, paths, sourceFiles, targetFiles, snapshot),
            CopyMode.UpdateSync => _updateSyncPlanBuilder.Build(
                request,
                paths,
                sourceFiles,
                targetFiles,
                snapshot),
            CopyMode.TwoWaySync => _twoWaySyncPlanBuilder.Build(
                request,
                paths,
                sourceFiles,
                targetFiles,
                snapshot),
            _ => _copyPlanBuilder.Build(request, paths, sourceFiles, targetFiles),
        };
        plan = AppendEmptyDirectoryOperations(plan, paths, sourceEmptyDirectories);
        ValidateDeleteSafety(plan, Math.Max(sourceFiles.Count, targetFiles.Count));
        _diskSpaceValidator.Validate(plan);
        return plan;
    }

    private async Task<CopyPlan> CreateMultiSourcePlanAsync(
        CopyTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Mode is not (CopyMode.FullCopy or CopyMode.IncrementalCopy
                or CopyMode.AccumulateSync or CopyMode.VerifyOnly))
            throw new CopyTaskValidationException("多源任务仅支持普通复制、增量复制、累加同步和仅校验模式。");
        if (string.IsNullOrWhiteSpace(request.TargetDirectory))
            throw new CopyTaskValidationException("请选择目标目录。");
        var targetRoot = _pathSafetyValidator.ValidateTargetDirectory(request.TargetDirectory);
        var rawSources = request.EffectiveSourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (rawSources.Length == 0) throw new CopyTaskValidationException("请选择至少一个源文件或源目录。");
        ValidateVersioning(request, targetRoot, rawSources
            .Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var items = new List<CopyPlanItem>();
        var usedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedFileTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in rawSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(source))
            {
                var prefix = UniqueName(Path.GetFileName(Path.TrimEndingDirectorySeparator(source)), usedPrefixes);
                var perTarget = Path.Combine(targetRoot, prefix);
                var perRequest = request with { SourceDirectory = source, TargetDirectory = perTarget, SourcePaths = [source] };
                var paths = _pathSafetyValidator.Validate(perRequest);
                var files = await _directoryScanner.ScanAsync(source, request.EffectiveFilters, cancellationToken,
                    hashMode: ToHashMode(request.VerificationMode));
                var plan = _copyPlanBuilder.Build(perRequest, paths, files);
                items.AddRange(plan.Items.Select(item => item with { RelativePath = Path.Combine(prefix, item.RelativePath) }));
                var emptyDirectories = await _directoryScanner.ScanEmptyDirectoriesAsync(source, request.EffectiveFilters, cancellationToken);
                items.AddRange(emptyDirectories.Select(directory => new CopyPlanItem(
                    Path.Combine(prefix, directory.RelativePath), directory.FullPath,
                    Path.Combine(perTarget, directory.RelativePath), 0,
                    Directory.GetLastWriteTimeUtc(directory.FullPath), CopyAction.CreateDirectory,
                    "多源任务同步空目录") { IsDirectory = true })
                    .Where(item => !Directory.Exists(item.TargetPath)));
            }
            else if (File.Exists(source))
            {
                var info = new FileInfo(source);
                var matcher = new FileFilterMatcher();
                if (!matcher.IsMatch(info, info.Name, request.EffectiveFilters)) continue;
                var targetName = UniqueFileName(info.Name, targetRoot, usedFileTargets);
                var targetPath = Path.Combine(targetRoot, targetName);
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                    throw new CopyTaskValidationException("目标目录会让源文件覆盖自身，请选择其他目标目录。");
                var entry = new FileEntry(targetName, source, info.Length, info.LastWriteTimeUtc)
                {
                    Attributes = info.Attributes,
                    ContentHash = await ComputeContentHashAsync(source, request.VerificationMode, cancellationToken),
                };
                var fileRequest = request with
                {
                    SourceDirectory = info.DirectoryName!, TargetDirectory = targetRoot, SourcePaths = [source],
                    ExistingFilePolicy = targetName == info.Name ? request.ExistingFilePolicy : ExistingFilePolicy.ModeDefault,
                };
                var paths = new ValidatedPaths(info.DirectoryName!, targetRoot);
                items.Add(_copyPlanBuilder.Build(fileRequest, paths, [entry]).Items.Single());
            }
            else throw new CopyTaskValidationException($"源路径不存在或当前无法访问：{source}。");
        }
        var normalizedRequest = request with
        {
            SourceDirectory = rawSources[0], SourcePaths = rawSources, TargetDirectory = targetRoot,
        };
        var combined = new CopyPlan(normalizedRequest, items, DateTime.UtcNow);
        _diskSpaceValidator.Validate(combined);
        return combined;
    }

    private static ContentHashMode ToHashMode(VerificationMode mode) => mode switch
    {
        VerificationMode.Sha256 => ContentHashMode.Sha256,
        VerificationMode.SampledSha256 => ContentHashMode.SampledSha256,
        _ => ContentHashMode.None,
    };

    private static async Task<string?> ComputeContentHashAsync(
        string path,
        VerificationMode mode,
        CancellationToken cancellationToken) => mode switch
        {
            VerificationMode.Sha256 => await ContentHasher.ComputeSha256HexAsync(path, cancellationToken),
            VerificationMode.SampledSha256 => await ContentHasher.ComputeSampledSha256HexAsync(path, cancellationToken),
            _ => null,
        };

    private static void ValidateVersioning(
        CopyTaskRequest request,
        string targetDirectory,
        IEnumerable<string> sourceDirectories)
    {
        var policy = request.EffectiveVersioning.Normalize();
        if (!policy.PreserveOverwrittenFiles) return;
        if (policy.VersionsDirectory is null)
            throw new CopyTaskValidationException("启用覆盖前版本保留时必须配置独立的版本目录。");

        var versionsDirectory = Path.TrimEndingDirectorySeparator(policy.VersionsDirectory);
        var root = Path.GetPathRoot(versionsDirectory);
        if (!string.IsNullOrWhiteSpace(root) && PathEquals(versionsDirectory, root))
            throw new CopyTaskValidationException("版本目录不能是磁盘根目录。");
        if (PathEquals(versionsDirectory, targetDirectory) || IsDescendant(versionsDirectory, targetDirectory))
            throw new CopyTaskValidationException("版本目录不能等于或位于目标目录内部，否则版本文件可能被再次同步。");
        if (sourceDirectories.Any(source => PathEquals(versionsDirectory, source)
                || IsDescendant(versionsDirectory, source)))
            throw new CopyTaskValidationException("版本目录不能等于或位于源目录内部，否则版本文件可能被再次复制。");
    }

    private static bool IsDescendant(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative != "." && !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathEquals(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);

    private static string UniqueName(string requested, ISet<string> used)
    {
        var basis = string.IsNullOrWhiteSpace(requested) ? "source" : requested;
        if (used.Add(basis)) return basis;
        for (var index = 2; ; index++)
        {
            var candidate = $"{basis} ({index})";
            if (used.Add(candidate)) return candidate;
        }
    }

    private static string UniqueFileName(string requested, string targetRoot, ISet<string> used)
    {
        if (used.Add(requested)) return requested;
        var stem = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);
        for (var index = 2; ; index++)
        {
            var candidate = $"{stem} ({index}){extension}";
            if (used.Add(candidate) && !Directory.Exists(Path.Combine(targetRoot, candidate))) return candidate;
        }
    }

    public async Task SaveSuccessfulSnapshotAsync(
        CopyTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode is not CopyMode.OneWaySync and not CopyMode.UpdateSync and not CopyMode.TwoWaySync)
        {
            return;
        }

        if (request.SynchronizationId is null || _snapshotStore is null)
        {
            throw new InvalidOperationException("同步快照存储或同步标识不可用。");
        }

        var paths = _pathSafetyValidator.Validate(request);
        var sourceFiles = await _directoryScanner.ScanAsync(
            paths.SourceDirectory,
            request.EffectiveFilters,
            cancellationToken,
            hashMode: ToHashMode(request.VerificationMode));
        var targetFiles = Directory.Exists(paths.TargetDirectory)
            ? await _directoryScanner.ScanAsync(
                paths.TargetDirectory,
                request.EffectiveFilters,
                cancellationToken,
                hashMode: ToHashMode(request.VerificationMode))
            : [];
        var states = BuildSnapshot(sourceFiles, targetFiles);
        await _snapshotStore.ReplaceSnapshotAsync(
            request.SynchronizationId.Value,
            states,
            cancellationToken);
    }

    private static IReadOnlyList<SyncFileState> BuildSnapshot(
        IReadOnlyList<FileEntry> sourceFiles,
        IReadOnlyList<FileEntry> targetFiles)
    {
        var source = sourceFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var target = targetFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        return source.Keys
            .Concat(target.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                source.TryGetValue(path, out var sourceFile);
                target.TryGetValue(path, out var targetFile);
                return new SyncFileState(
                    path,
                    sourceFile is not null,
                    sourceFile?.Length,
                    sourceFile?.LastWriteTimeUtc,
                    targetFile is not null,
                    targetFile?.Length,
                    targetFile?.LastWriteTimeUtc,
                    sourceFile?.ContentHash,
                    targetFile?.ContentHash);
            })
            .ToArray();
    }

    private static CopyPlan AppendEmptyDirectoryOperations(
        CopyPlan plan,
        ValidatedPaths paths,
        IReadOnlyList<EmptyDirectoryEntry> sourceEmptyDirectories)
    {
        if (plan.Request.Mode == CopyMode.VerifyOnly)
        {
            return plan;
        }

        var additions = sourceEmptyDirectories
            .Select(directory => new CopyPlanItem(
                directory.RelativePath,
                directory.FullPath,
                Path.Combine(paths.TargetDirectory, directory.RelativePath),
                0,
                Directory.GetLastWriteTimeUtc(directory.FullPath),
                CopyAction.CreateDirectory,
                Directory.Exists(Path.Combine(paths.TargetDirectory, directory.RelativePath))
                    ? "空目录已存在"
                    : "同步空目录")
            {
                IsDirectory = true,
            })
            .Where(item => !Directory.Exists(item.TargetPath))
            .ToArray();
        return additions.Length == 0
            ? plan
            : plan with { Items = [.. plan.Items, .. additions] };
    }

    private static void ValidateDeleteSafety(CopyPlan plan, int largerSideFileCount)
    {
        if (plan.FilesToQuarantine == 0)
        {
            return;
        }

        var policy = plan.Request.EffectiveDeleteSafetyPolicy.Normalize();
        if (plan.FilesToQuarantine > policy.MaximumItems)
        {
            throw new CopyTaskValidationException(
                $"任务需要隔离 {plan.FilesToQuarantine:N0} 个文件，超过安全上限 {policy.MaximumItems:N0} 个。"
                + "请检查路径或显式调整删除安全阈值。");
        }

        var percentage = largerSideFileCount == 0
            ? 0
            : plan.FilesToQuarantine * 100d / largerSideFileCount;
        if (percentage > policy.MaximumPercentage)
        {
            throw new CopyTaskValidationException(
                $"任务需要隔离两端较大目录 {percentage:0.##}% 的文件，超过安全上限 {policy.MaximumPercentage:0.##}%。"
                + "请检查路径或显式调整删除安全阈值。");
        }
    }
}
