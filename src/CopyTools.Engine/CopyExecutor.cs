using CopyTools.Domain;

namespace CopyTools.Engine;

public sealed class CopyExecutor
{
    private readonly SafeFileCopier _safeFileCopier;
    private readonly QuarantineService _quarantineService;
    private readonly FileVersionService _fileVersionService;
    private readonly IReadOnlyDictionary<string, IFileVerificationProvider> _verificationProviders;

    public CopyExecutor(
        SafeFileCopier? safeFileCopier = null,
        QuarantineService? quarantineService = null,
        FileVersionService? fileVersionService = null,
        IReadOnlyDictionary<string, IFileVerificationProvider>? verificationProviders = null)
    {
        _safeFileCopier = safeFileCopier ?? new SafeFileCopier();
        _quarantineService = quarantineService ?? new QuarantineService();
        _fileVersionService = fileVersionService ?? new FileVersionService();
        _verificationProviders = verificationProviders
            ?? new Dictionary<string, IFileVerificationProvider>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<CopyExecutionResult> ExecuteAsync(
        CopyPlan plan,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        CopyExecutionControl? executionControl = null,
        Func<CopyPlanItem, CopyItemExecutionResult, CancellationToken, Task>? itemCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var startedAtUtc = DateTime.UtcNow;
        var failures = new List<CopyItemFailure>();
        var itemResults = new List<CopyItemExecutionResult>();
        var quarantinedFiles = new List<QuarantinedFileResult>();
        var failedCopyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completedOperations = 0;
        var successfulOperations = 0;
        long copiedBytes = 0;

        var executableItems = plan.Items.Where(item => item.WillExecute).ToArray();
        var effectivePerformance = CopyPerformanceTuner.Resolve(plan);
        var canRunParallel = effectivePerformance.MaxParallelFiles > 1
            && effectivePerformance.SpeedLimitBytesPerSecond is null
            && executableItems.All(item => item.Action is
                CopyAction.CopyNew or CopyAction.Overwrite or CopyAction.RenameCopy
                or CopyAction.ConflictCopy or CopyAction.Verify or CopyAction.CreateDirectory);
        if (canRunParallel)
        {
            try
            {
                await Parallel.ForEachAsync(
                    executableItems,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = effectivePerformance.MaxParallelFiles,
                    },
                    async (item, itemCancellationToken) =>
                    {
                        if (executionControl is not null)
                        {
                            await executionControl.WaitIfPausedAsync(itemCancellationToken);
                        }

                        var completedSnapshot = Volatile.Read(ref completedOperations);
                        var copiedSnapshot = Interlocked.Read(ref copiedBytes);
                        try
                        {
                            var attempts = await ExecuteItemWithRetryAsync(
                                plan,
                                item,
                                copiedSnapshot,
                                completedSnapshot,
                                progress,
                                quarantinedFiles,
                                itemCancellationToken,
                                executionControl,
                                effectivePerformance);
                            if (item.WillCopy)
                            {
                                Interlocked.Add(ref copiedBytes, item.Length);
                            }

                            Interlocked.Increment(ref successfulOperations);
                            var itemResult = new CopyItemExecutionResult(
                                item.RelativePath,
                                item.Action,
                                "成功",
                                item.WillCopy ? item.Length : 0,
                                null,
                                attempts);
                            lock (itemResults)
                            {
                                itemResults.Add(itemResult);
                            }
                            if (itemCompleted is not null)
                            {
                                await itemCompleted(item, itemResult, itemCancellationToken);
                            }
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            var attempts = plan.Request.EffectiveRetryPolicy.Normalize().MaxAttempts;
                            lock (failures)
                            {
                                failures.Add(new CopyItemFailure(item.RelativePath, exception.Message, attempts));
                            }

                            lock (itemResults)
                            {
                                itemResults.Add(new CopyItemExecutionResult(
                                    item.RelativePath,
                                    item.Action,
                                    "失败",
                                    0,
                                    exception.Message,
                                    attempts));
                            }
                        }
                        finally
                        {
                            Interlocked.Increment(ref completedOperations);
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                return CreateResult(wasCancelled: true);
            }

            progress?.Report(new CopyProgress(
                completedOperations,
                plan.OperationsToExecute,
                copiedBytes,
                plan.BytesToCopy,
                string.Empty,
                failures.Count == 0 ? "执行完成" : "部分操作失败"));
            return CreateResult(wasCancelled: false);
        }

        foreach (var item in executableItems)
        {
            if (executionControl is not null)
            {
                await executionControl.WaitIfPausedAsync(cancellationToken);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateResult(wasCancelled: true);
            }

            if (item.Action == CopyAction.RemoveSource
                && failedCopyPaths.Contains(item.RelativePath))
            {
                const string error = "前置复制失败，为保护源文件，未执行源端清理。";
                failures.Add(new CopyItemFailure(item.RelativePath, error, 0));
                itemResults.Add(new CopyItemExecutionResult(
                    item.RelativePath,
                    item.Action,
                    "失败",
                    0,
                    error,
                    0));
                completedOperations++;
                continue;
            }

            progress?.Report(new CopyProgress(
                completedOperations,
                plan.OperationsToExecute,
                copiedBytes,
                plan.BytesToCopy,
                item.RelativePath,
                DescribeAction(item.Action)));

            try
            {
                var attempts = await ExecuteItemWithRetryAsync(
                    plan,
                    item,
                    copiedBytes,
                    completedOperations,
                    progress,
                    quarantinedFiles,
                    cancellationToken,
                    executionControl,
                    effectivePerformance);

                if (item.WillCopy)
                {
                    copiedBytes += item.Length;
                }

                successfulOperations++;
                var itemResult = new CopyItemExecutionResult(
                    item.RelativePath,
                    item.Action,
                    "成功",
                    item.WillCopy ? item.Length : 0,
                    null,
                    attempts);
                itemResults.Add(itemResult);
                if (itemCompleted is not null)
                {
                    await itemCompleted(item, itemResult, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return CreateResult(wasCancelled: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var attempts = plan.Request.EffectiveRetryPolicy.Normalize().MaxAttempts;
                failures.Add(new CopyItemFailure(item.RelativePath, exception.Message, attempts));
                itemResults.Add(new CopyItemExecutionResult(
                    item.RelativePath,
                    item.Action,
                    "失败",
                    0,
                    exception.Message,
                    attempts));
                if (item.WillCopy)
                {
                    failedCopyPaths.Add(item.RelativePath);
                }
            }

            completedOperations++;
        }

        progress?.Report(new CopyProgress(
            completedOperations,
            plan.OperationsToExecute,
            copiedBytes,
            plan.BytesToCopy,
            string.Empty,
            failures.Count == 0 ? "执行完成" : "部分操作失败"));

        return CreateResult(wasCancelled: false);

        CopyExecutionResult CreateResult(bool wasCancelled) =>
            new(
                successfulOperations,
                plan.SkippedFiles,
                copiedBytes,
                failures,
                wasCancelled,
                startedAtUtc,
                DateTime.UtcNow,
                BuildResultItems(),
                quarantinedFiles);

        IReadOnlyList<CopyItemExecutionResult> BuildResultItems()
        {
            var skippedItems = plan.Items
                .Where(item => !item.WillExecute)
                .Select(item => new CopyItemExecutionResult(
                    item.RelativePath,
                    item.Action,
                    "跳过",
                    0,
                    null,
                    0));
            return [.. itemResults, .. skippedItems];
        }
    }

    private async Task<int> ExecuteItemWithRetryAsync(
        CopyPlan plan,
        CopyPlanItem item,
        long copiedBytes,
        int completedOperations,
        IProgress<CopyProgress>? progress,
        ICollection<QuarantinedFileResult> quarantinedFiles,
        CancellationToken cancellationToken,
        CopyExecutionControl? executionControl,
        PerformanceOptions effectivePerformance)
    {
        var retryPolicy = plan.Request.EffectiveRetryPolicy.Normalize();
        var attempts = 0;

        while (true)
        {
            attempts++;
            try
            {
                switch (item.Action)
                {
                    case CopyAction.CopyNew:
                    case CopyAction.Overwrite:
                    case CopyAction.ConflictCopy:
                    case CopyAction.RenameCopy:
                        await ExecuteCopyAsync(
                            plan,
                            item,
                            copiedBytes,
                            completedOperations,
                            attempts,
                            progress,
                            cancellationToken,
                            executionControl,
                            effectivePerformance);
                        break;

                    case CopyAction.Verify:
                        await VerifyAsync(item, plan.Request, cancellationToken);
                        break;

                    case CopyAction.CreateDirectory:
                        Directory.CreateDirectory(item.TargetPath);
                        Directory.SetLastWriteTimeUtc(item.TargetPath, item.LastWriteTimeUtc);
                        break;

                    case CopyAction.QuarantineTarget:
                    case CopyAction.QuarantineSource:
                        AddQuarantineResult(
                            _quarantineService.Quarantine(plan, item, DateTime.UtcNow),
                            quarantinedFiles);
                        break;

                    case CopyAction.RemoveSource:
                        ValidateMoveTarget(item, plan.Request.VerificationMode);
                        AddQuarantineResult(
                            _quarantineService.Quarantine(plan, item, DateTime.UtcNow),
                            quarantinedFiles);
                        break;

                    default:
                        throw new InvalidOperationException($"不支持执行操作：{item.Action}");
                }

                return attempts;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException
                && attempts < retryPolicy.MaxAttempts)
            {
                progress?.Report(new CopyProgress(
                    completedOperations,
                    plan.OperationsToExecute,
                    copiedBytes,
                    plan.BytesToCopy,
                    item.RelativePath,
                    $"操作失败，等待重试（{attempts}/{retryPolicy.MaxAttempts}）"));
                if (!PathsAvailable(plan.Request))
                    await WaitForPathsAsync(plan.Request, retryPolicy.AvailabilityWaitTimeout ?? TimeSpan.Zero,
                        cancellationToken);
                else
                    await Task.Delay(retryPolicy.Delay, cancellationToken);
            }
        }
    }

    private static bool PathsAvailable(CopyTaskRequest request) =>
        request.EffectiveSourcePaths.All(path => File.Exists(path) || Directory.Exists(path))
        && (Directory.Exists(request.TargetDirectory)
            || Directory.Exists(Path.GetDirectoryName(request.TargetDirectory)));

    private static async Task WaitForPathsAsync(
        CopyTaskRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) return;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsAvailable(request)) return;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task ExecuteCopyAsync(
        CopyPlan plan,
        CopyPlanItem item,
        long copiedBytes,
        int completedOperations,
        int attempts,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken,
        CopyExecutionControl? executionControl,
        PerformanceOptions effectivePerformance)
    {
        ValidatePlanItemIsStillCurrent(item);
        if (item.Action == CopyAction.Overwrite && attempts == 1)
        {
            _fileVersionService.PreserveBeforeOverwrite(
                item.TargetPath, item.RelativePath, plan.Request.EffectiveVersioning, DateTime.UtcNow);
        }
        var bytesForCurrentFile = 0L;
        var byteProgress = new InlineProgress<long>(increment =>
        {
            bytesForCurrentFile += increment;
            progress?.Report(new CopyProgress(
                completedOperations,
                plan.OperationsToExecute,
                copiedBytes + bytesForCurrentFile,
                plan.BytesToCopy,
                item.RelativePath,
                attempts == 1 ? "正在复制" : $"正在重试（第 {attempts} 次）",
                bytesForCurrentFile,
                item.Length));
        });

        if (executionControl is not null)
        {
            await executionControl.WaitIfPausedAsync(cancellationToken);
        }
        await _safeFileCopier.CopyAsync(
            item.SourcePath,
            item.TargetPath,
            item.LastWriteTimeUtc,
            item.SourceAttributes,
            item.Action == CopyAction.Overwrite,
            plan.Request.VerificationMode,
            byteProgress,
            cancellationToken,
            executionControl,
            effectivePerformance,
            plan.Request.EffectiveSecurity,
            plan.Request.TransferMode);
        await VerifyWithPluginAsync(item.SourcePath, item.TargetPath, plan.Request, cancellationToken);
    }

    private async Task VerifyAsync(
        CopyPlanItem item,
        CopyTaskRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePlanItemIsStillCurrent(item);
        if (!File.Exists(item.TargetPath))
        {
            throw new IOException("目标文件不存在，校验失败。");
        }

        var target = new FileInfo(item.TargetPath);
        if (target.Length != item.Length)
        {
            throw new IOException("源文件和目标文件大小不一致，校验失败。");
        }

        if (request.VerificationMode is VerificationMode.Sha256 or VerificationMode.SampledSha256)
        {
            var sourceHash = await ComputeContentHashAsync(item.SourcePath, request.VerificationMode, cancellationToken);
            var targetHash = await ComputeContentHashAsync(item.TargetPath, request.VerificationMode, cancellationToken);
            if (!sourceHash.AsSpan().SequenceEqual(targetHash))
            {
                throw new IOException("源文件和目标文件 SHA-256 内容哈希不一致，校验失败。");
            }
        }
        await VerifyWithPluginAsync(item.SourcePath, item.TargetPath, request, cancellationToken);
    }

    private async Task VerifyWithPluginAsync(
        string sourcePath,
        string targetPath,
        CopyTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.VerificationProviderId)) return;
        if (!_verificationProviders.TryGetValue(request.VerificationProviderId, out var provider))
            throw new IOException($"任务要求的插件校验器未加载：{request.VerificationProviderId}。");
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (!await provider.VerifyAsync(source, target, cancellationToken))
            throw new IOException($"插件校验失败：{provider.DisplayName}。");
    }

    private static async Task<byte[]> ComputeContentHashAsync(
        string path,
        VerificationMode verificationMode,
        CancellationToken cancellationToken) =>
        verificationMode == VerificationMode.SampledSha256
            ? await ContentHasher.ComputeSampledSha256Async(path, cancellationToken)
            : await ComputeHashAsync(path, cancellationToken);

    private static async Task<byte[]> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static void ValidatePlanItemIsStillCurrent(CopyPlanItem item)
    {
        if (!File.Exists(item.SourcePath))
        {
            throw new IOException("源文件在生成预览后已不存在，请重新生成预览。");
        }

        var currentSource = new FileInfo(item.SourcePath);
        var sourceChanged = currentSource.Length != item.Length
            || Math.Abs((currentSource.LastWriteTimeUtc - item.LastWriteTimeUtc).TotalSeconds) > 2;
        if (sourceChanged)
        {
            throw new IOException("源文件在生成预览后已发生变化，请重新生成预览。");
        }

        if (item.Action is CopyAction.CopyNew or CopyAction.ConflictCopy or CopyAction.RenameCopy
            && File.Exists(item.TargetPath))
        {
            throw new IOException("目标文件在生成预览后已出现，为避免未经确认的覆盖，请重新生成预览。");
        }
    }

    private static void ValidateMoveTarget(CopyPlanItem item, VerificationMode verificationMode)
    {
        if (!File.Exists(item.TargetPath))
        {
            throw new IOException("移动目标文件不存在，未清理源文件。");
        }

        var source = new FileInfo(item.SourcePath);
        var target = new FileInfo(item.TargetPath);
        if (source.Length != target.Length)
        {
            throw new IOException("移动目标文件大小与源文件不一致，未清理源文件。");
        }

        if (verificationMode is VerificationMode.Sha256 or VerificationMode.SampledSha256)
        {
            using var sourceStream = source.OpenRead();
            using var targetStream = target.OpenRead();
            var sourceHash = verificationMode == VerificationMode.SampledSha256
                ? ContentHasher.ComputeSampledSha256(sourceStream)
                : System.Security.Cryptography.SHA256.HashData(sourceStream);
            var targetHash = verificationMode == VerificationMode.SampledSha256
                ? ContentHasher.ComputeSampledSha256(targetStream)
                : System.Security.Cryptography.SHA256.HashData(targetStream);
            if (!sourceHash.AsSpan().SequenceEqual(targetHash))
            {
                throw new IOException("移动目标文件内容哈希与源文件不一致，未清理源文件。");
            }
        }
    }

    private static void AddQuarantineResult(
        QuarantineOperationResult result,
        ICollection<QuarantinedFileResult> quarantinedFiles) =>
        quarantinedFiles.Add(new QuarantinedFileResult(
            result.OriginalPath,
            result.QuarantinePath,
            result.RelativePath,
            result.Length,
            result.QuarantinedAtUtc));

    private static string DescribeAction(CopyAction action) => action switch
    {
        CopyAction.CopyNew or CopyAction.Overwrite or CopyAction.RenameCopy => "正在复制",
        CopyAction.ConflictCopy => "正在保存冲突副本",
        CopyAction.QuarantineTarget => "正在隔离目标文件",
        CopyAction.QuarantineSource => "正在隔离源文件",
        CopyAction.RemoveSource => "正在安全清理源文件",
        CopyAction.Verify => "正在校验",
        CopyAction.CreateDirectory => "正在创建空目录",
        _ => "正在处理",
    };

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
