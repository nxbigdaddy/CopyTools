using System.Text;
using System.Runtime.Versioning;
using CopyTools.Application;
using CopyTools.Domain;
using CopyTools.Engine;
using CopyTools.Infrastructure;

[assembly: SupportedOSPlatform("windows")]

var tests = new (string Name, Func<Task> Run)[]
{
    ("空目标生成新增计划", EmptyTargetCreatesNewItems),
    ("增量复制跳过已有文件", IncrementalCopySkipsExistingFiles),
    ("累加同步覆盖变化并跳过未变化", AccumulateSyncDetectsChanges),
    ("普通复制覆盖已有文件", FullCopyOverwritesExistingFiles),
    ("阻止目标目录位于源目录内部", RejectsNestedTargetDirectory),
    ("阻止源目录与目标目录相同", RejectsIdenticalDirectories),
    ("执行器安全复制内容并保留时间", ExecutorCopiesContentAndTimestamp),
    ("执行后重新预览可识别未变化文件", ReplanAfterExecutionSkipsUnchangedFiles),
    ("拒绝执行预览后发生变化的源文件", RejectsSourceChangedAfterPreview),
    ("拒绝覆盖预览后新出现的目标文件", RejectsTargetAppearingAfterPreview),
    ("过滤规则按模式目录和大小筛选文件", FiltersSelectExpectedFiles),
    ("SHA-256 严格校验复制成功", Sha256VerificationCopiesFile),
    ("SHA-256 变化判断发现同大小同时间内容差异", Sha256DetectsSameMetadataContentChange),
    ("SQLite 保存并读取中文任务配置", SqliteTaskRoundTrip),
    ("SQLite 原子保存并读取运行明细", SqliteRunHistoryRoundTrip),
    ("镜像同步将目标多余文件移入隔离区", MirrorSyncQuarantinesExtraTarget),
    ("镜像同步超过删除比例时阻止生成计划", MirrorSyncDeleteThresholdBlocksPlan),
    ("移动同步复制成功后隔离源文件", MoveSyncQuarantinesSourceAfterCopy),
    ("移动同步复制失败时保留源文件", MoveSyncRetainsSourceWhenCopyFails),
    ("隔离记录可恢复并更新数据库状态", QuarantineRestoreRoundTrip),
    ("SQLite 同步快照可完整往返", SyncSnapshotRoundTrip),
    ("单向同步传播源端删除并保留目标端额外文件", OneWaySyncPropagatesManagedDeletionOnly),
    ("更新同步只发布变化文件组成的新批次", UpdateSyncPublishesChangedBatch),
    ("更新同步无变化时可提交空计划基线", UpdateSyncCommitsEmptyPlan),
    ("双向同步传播目标端修改", TwoWaySyncPropagatesTargetChange),
    ("双向同步传播删除并隔离另一端文件", TwoWaySyncPropagatesDeletionSafely),
    ("双向同步为并发修改保留双份冲突副本", TwoWaySyncPreservesConflictCopies),
    ("同步执行失败时不更新快照", FailedSyncDoesNotAdvanceSnapshot),
    ("计划计算支持每日每周每月和固定间隔", ScheduleCalculatorSupportsTriggers),
    ("SQLite 计划配置可完整往返", ScheduleRoundTrip),
    ("自动任务默认阻止未经授权的覆盖", ScheduledRunBlocksUnapprovedOverwrite),
    ("自动任务授权后执行并保存历史", ScheduledRunExecutesAndSavesHistory),
    ("错过计划的跳过策略不会执行任务", MissedScheduleSkipDoesNotExecute),
    ("仅校验模式不写入并报告哈希不一致", VerifyOnlyDetectsMismatchWithoutWriting),
    ("仅校验模式将缺失目标报告为失败", VerifyOnlyReportsMissingTarget),
    ("复制保留隐藏只读和归档属性", CopyPreservesFileAttributes),
    ("暂停控制阻止复制并可继续完成", PauseAndResumeControlsCopy),
    ("失败项可以单独重试", FailedItemsCanBeRetried),
    ("同步会保留空目录结构", SyncPreservesEmptyDirectories),
    ("同名文件可自动重命名复制", ExistingFileCanBeRenamed),
    ("同名文件可按策略跳过", ExistingFileCanBeSkippedByPolicy),
    ("预览计划可排除指定项", PlanCanExcludeSelectedItems),
    ("任务和计划可导出后导入", TaskBundleExportImportRoundTrip),
    ("运行日志可导出 JSON CSV 和文本", RunLogsExportInThreeFormats),
    ("历史增量备份复用未变化对象并可按时间点恢复", IncrementalBackupReusesObjectsAndRestoresSnapshot),
    ("完全备份为每次快照写入独立对象", FullBackupCreatesIndependentObjects),
    ("备份可选择性恢复指定文件", BackupRestoresSelectedFiles),
    ("备份保留清理不会破坏仍被引用的对象", BackupRetentionPreservesReferencedObjects),
    ("备份恢复保留原始文件属性且版本对象可清理", BackupPreservesAttributesWithoutLockingObjects),
    ("复制缓冲区与速度限制配置会归一化", PerformanceOptionsNormalizeSafely),
    ("性能自动调优按文件规模限制并发", CopyPerformanceAutoTuneUsesSafeLimits),
    ("后台性能策略限制并发与带宽", BackgroundPerformancePolicyLowersImpact),
    ("复制速度限制会实际节流传输", SpeedLimitThrottlesCopy),
    ("文件变化触发会去抖后执行完整差异扫描", FileChangeTriggerDebouncesAndRunsTask),
    ("应用设置可原子保存读取且损坏后回退默认", SettingsStoreRoundTripAndCorruptionRecovery),
    ("非破坏性任务支持受限并发且全部结果可追踪", ParallelCopyCompletesAllItems),
    ("自动触发规则可持久化并随任务删除", AutoTriggerRulePersistsAndCascades),
    ("本地存储适配器阻止越界并支持流式传输", LocalStorageAdapterTransfersSafely),
    ("统一存储传输服务复制完整目录树", StorageTransferCopiesTree),
    ("Windows 凭据存储不会将秘密写入普通文件", WindowsCredentialStoreRoundTrip),
    ("存储连接仅持久化凭据引用且可测试本地连接", StorageConnectionPersistsReferenceAndTestsLocal),
    ("WebDAV 适配器支持列举读取写入移动和删除", WebDavAdapterSupportsCoreOperations),
    ("OneDrive 适配器使用 Graph 路径接口和分片上传", OneDriveAdapterUsesGraphAndChunkUpload),
    ("Google Drive 适配器解析文件 ID 并使用可恢复上传", GoogleDriveAdapterResolvesIdsAndResumableUpload),
    ("Dropbox 适配器使用路径 API 和上传会话", DropboxAdapterUsesPathApiAndUploadSession),
    ("S3 适配器生成 SigV4 并执行分片上传", S3AdapterSignsAndUsesMultipartUpload),
    ("百度网盘适配器执行三段式分片上传和文件操作", BaiduNetdiskAdapterUsesThreePhaseUpload),
    ("任务编排元数据可持久化且受控命令不经过 shell 拼接", TaskOrchestrationMetadataAndControlledCommand),
    ("任务队列按依赖执行并保留每项结果", TaskQueueHonorsDependencies),
    ("大文件取消后保留有效分块并从断点继续", LargeFileResumesFromPartial),
    ("任务检查点仅跳过来源和目标仍匹配的成功项", ExecutionCheckpointResumesSafely),
    ("压缩加密备份可恢复且错误密码和篡改会失败", SecureArchiveBackupAuthenticatesAndRestores),
    ("Webhook 通知仅发送摘要且凭据只进入授权头", WebhookNotificationSendsSummaryWithoutSecretBody),
    ("路径模板安全展开日期任务名和允许的环境变量", PathTemplateExpandsSafely),
    ("覆盖前旧版本可保留并恢复", OverwriteVersionCanBePreservedAndRestored),
    ("覆盖版本目录必须与复制目录隔离", VersionDirectoryMustBeIsolated),
    ("复制可选保留 Windows 文件访问控制表", CopyCanPreserveWindowsAcl),
    ("插件目录隔离加载存储工厂并执行追加校验", PluginCatalogLoadsAndVerifierRuns),
    ("备份清单可记录快照之外的原始源目录", BackupManifestCanKeepOriginalSource),
    ("多源目录和文件复制使用隔离目标并避免同名碰撞", MultiSourceCopyUsesIsolatedTargets),
    ("多源任务拒绝具有删除语义的同步模式", MultiSourceRejectsDestructiveModes),
    ("过滤与任务模板可持久化并重新读取", TemplatesPersistRoundTrip),
    ("重复文件检测只报告内容相同文件", DuplicateDetectorHashesCandidates),
    ("完整配置备份恢复排除秘密并保留编排关系", ConfigurationBackupRestoresWithoutSecrets),
    ("Windows 服务命令行参数可安全引用空格和尾斜杠", WindowsServiceArgumentsAreQuotedSafely),
    ("双向冲突询问策略必须解决后才能执行", TwoWayConflictMustBeResolvedBeforeExecution),
    ("符号链接策略默认安全跳过并可配置", ReparsePointPolicyControlsFileLinks),
    ("更新服务下载后严格校验大小和 SHA-256", UpdateServiceVerifiesLocalPackage),
    ("块级增量传输只重建发生变化的块", DeltaFileCopierReconstructsChangedBlocks),
    ("增量传输模式经执行器覆盖目标文件", DeltaCopyModeOverwritesViaExecutor),
    ("采样 SHA-256 发现同大小同时间的内容差异", SampledSha256DetectsSameMetadataContentChange),
    ("采样 SHA-256 校验复制成功", SampledSha256VerificationCopiesFile),
    ("备份完整性自检发现缺失并可自动修复", BackupIntegrityVerifiesAndRepairsMissingObject),
    ("不可变快照在保留清理中不被删除", ImmutableSnapshotSurvivesRetention),
    ("GFS 保留策略按日月年分桶保留", GfsRetentionKeepsPerBucket),
    ("恢复演练恢复并校验快照内容", RestoreDrillVerifiesSnapshot),
    ("应用状态清单采集机器信息", AppStateManifestCapturesMachineInfo),
    ("运行条件评估器判定文件与磁盘条件", RunConditionEvaluatorEvaluatesConditions),
    ("计划任务在条件不满足时跳过执行", ScheduledRunSkipsWhenConditionFails),
    ("计划任务在维护窗口外跳过执行", ScheduledRunSkipsOutsideTimeWindow),
    ("任务链把前置任务输出作为后继源", TaskChainRunsSuccessorWithOutputAsSource),
    ("自动触发规则归一化最小间隔", AutoTriggerRuleNormalizesMinInterval),
    ("计划条件与时间窗口可持久化往返", ScheduleConditionsAndWindowRoundTrip),
    ("加密备份包不泄露原始文件名", EncryptedArchiveHidesFileNames),
    ("加密备份密码轮换后旧密码失效", ArchivePasswordRotationChangesKey),
    ("恢复码长度与字符集安全", RecoveryCodeIsStrong),
    ("防勒索检测器识别突发批量变更", RansomwareChangeDetectorFlagsBurst),
    ("审计日志哈希链检测篡改", AuditLogChainDetectsTampering),
    ("Azure Blob 适配器生成共享密钥授权", AzureBlobAdapterSignsSharedKeyRequests),
    ("通用网盘适配器生成承载令牌请求", GenericJsonCloudAdapterShapesBearerRequests),
    ("局域网直连传输协议压缩往返", LanFileTransferRoundTripsWithCompression),
    ("分时段限速按本地时间解析速率", TimeBasedSpeedLimiterResolvesSchedule),
    ("存储工厂把 MinIO 映射到 S3 兼容适配器", FactoryMapsMinioToS3Compatible),
    ("即时通讯通知按频道生成不同消息体", ImNotificationChannelsGenerateDistinctBodies),
    ("右键菜单集成生成带引号命令", ShellIntegrationBuildsEntries),
    ("目录差异服务输出并排差异行", DirectoryDiffServiceComparesTrees),
    ("仪表盘服务聚合运行历史概览", DashboardServiceAggregatesHistory),
    ("全局搜索跨任务运行与隔离区检索", GlobalSearchServiceFindsAcrossEntities),
    ("通知中心缓冲并维护已读状态", NotificationCenterBuffersAndMarksRead),
    ("本地化服务按语言解析并回退", LocalizationServiceResolvesLanguage),
    ("并行扫描为全部文件计算内容哈希", ParallelScanComputesAllHashes),
    ("历史归档导出旧记录并清理数据库", HistoryArchiveExportsAndCleans),
    ("大列表分页器支持虚拟化分页", LargeListPagerPaginates),
    ("失败聚合按错误类别统计共性原因", FailureAggregationGroupsByCategory),
    ("存储趋势按容量样本预测耗尽时间", StorageTrendPredictsDiskExhaustion),
    ("健康评分综合多维指标给出等级", HealthScoreRatesOverallHealth),
    ("Ollama 助手走 chat 端点并解析响应", OllamaAssistantUsesChatEndpoint),
    ("OpenAI 兼容助手带 Bearer 与 JSON 模式", OpenAiAssistantUsesBearerAndJsonMode),
    ("AI 输出可从 Markdown 围栏提取 JSON", AiJsonExtractsFromMarkdownFences),
    ("AI 助手工厂按配置选择并回退规则", AiAssistantFactoryFallsBackToRule),
    ("自然语言任务解析源目标模式与排除", NaturalLanguageTaskParsesDescription),
    ("自然语言任务使用助手 JSON 输出", NaturalLanguageTaskUsesAssistantJson),
    ("同步模式顾问说明破坏性后果", SyncModeAdvisorExplainsConsequences),
    ("自然语言过滤规则解析大小与目录", NaturalLanguageFilterParsesRules),
    ("帮助助手按关键词命中内置 FAQ", HelpAssistantAnswersFromFaq),
    ("风险解释把计划翻译成自然语言", RiskExplanationSummarizesPlan),
    ("冲突顾问按元数据给出解决建议", ConflictAdvisorRecommendsByMetadata),
    ("失败根因分析生成修复建议", FailureRootCauseProducesAdvice),
    ("重试顾问按失败类别给出策略", RetryAdvisorSuggestsNetworkRetry),
    ("文件分类器按扩展名归类", FileClassifierClassifiesByExtension),
    ("相似文件检测按名称与大小分组", SimilarFileDetectorGroupsByNameAndSize),
    ("敏感文件扫描识别凭据与隐私", SensitiveFileScannerFlagsSecrets),
    ("归档整理器按类别给出移动建议", ArchiveOrganizerSuggestsCategories),
    ("空间顾问预测磁盘耗尽并预警", SpaceAdvisorWarnsAboutDiskFull),
    ("健康报告把评分合成为自然语言", HealthReportNarratesScore),
    ("调度顾问避开历史失败时段", ScheduleAdvisorAvoidsFailureHour),
    ("耗时预估按历史吞吐率推算", DurationEstimatorEstimatesFromThroughput),
    ("异常检测器识别变更率突增", AnomalyDetectorFlagsSpike),
    ("演练解读报告备份可恢复性", DrillInterpreterReportsRecoverability),
    ("传输顾问为远端大文件推荐增量", TransferAdvisorRecommendsDeltaForLargeRemote),
};

var failed = 0;
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"CopyTools 核心测试：{tests.Length} 项");

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"[通过] {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"[失败] {test.Name}");
        Console.WriteLine($"       {exception}");
    }
}

Console.WriteLine();
Console.WriteLine(failed == 0
    ? $"全部通过：{tests.Length}/{tests.Length}"
    : $"测试失败：{failed}/{tests.Length}");

return failed == 0 ? 0 : 1;

static async Task EmptyTargetCreatesNewItems()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("first.txt", "first");
    workspace.WriteSource(Path.Combine("nested", "second.txt"), "second");

    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);

    Assert.Equal(2, plan.TotalFiles);
    Assert.Equal(2, plan.NewFiles);
    Assert.Equal(2, plan.FilesToCopy);
    Assert.True(plan.Items.All(item => item.Action == CopyAction.CopyNew));
}

static async Task IncrementalCopySkipsExistingFiles()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("existing.txt", "source-newer");
    workspace.WriteTarget("existing.txt", "target-old");

    var plan = await workspace.CreatePlan(CopyMode.IncrementalCopy);

    Assert.Equal(0, plan.FilesToCopy);
    Assert.Equal(CopyAction.SkipExisting, plan.Items.Single().Action);
}

static async Task AccumulateSyncDetectsChanges()
{
    using var workspace = TestWorkspace.Create();
    var unchangedTime = new DateTime(2026, 8, 13, 1, 2, 4, DateTimeKind.Utc);
    workspace.WriteSource("unchanged.txt", "same", unchangedTime);
    workspace.WriteTarget("unchanged.txt", "same", unchangedTime);
    workspace.WriteSource("changed.txt", "source-content", unchangedTime.AddMinutes(1));
    workspace.WriteTarget("changed.txt", "old", unchangedTime);

    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);

    Assert.Equal(2, plan.TotalFiles);
    Assert.Equal(1, plan.FilesToCopy);
    Assert.Equal(CopyAction.Overwrite, plan.Items.Single(item => item.RelativePath == "changed.txt").Action);
    Assert.Equal(CopyAction.SkipUnchanged, plan.Items.Single(item => item.RelativePath == "unchanged.txt").Action);
}

static async Task FullCopyOverwritesExistingFiles()
{
    using var workspace = TestWorkspace.Create();
    var timestamp = new DateTime(2026, 8, 13, 2, 0, 0, DateTimeKind.Utc);
    workspace.WriteSource("same.txt", "same", timestamp);
    workspace.WriteTarget("same.txt", "same", timestamp);

    var plan = await workspace.CreatePlan(CopyMode.FullCopy);

    Assert.Equal(1, plan.FilesToCopy);
    Assert.Equal(CopyAction.Overwrite, plan.Items.Single().Action);
}

static async Task RejectsNestedTargetDirectory()
{
    using var workspace = TestWorkspace.Create();
    var nestedTarget = Path.Combine(workspace.SourceDirectory, "nested-target");

    await Assert.ThrowsAsync<CopyTaskValidationException>(() =>
        new CopyPlanService().CreatePlanAsync(
            new CopyTaskRequest(workspace.SourceDirectory, nestedTarget, CopyMode.AccumulateSync)));
}

static async Task RejectsIdenticalDirectories()
{
    using var workspace = TestWorkspace.Create();

    await Assert.ThrowsAsync<CopyTaskValidationException>(() =>
        new CopyPlanService().CreatePlanAsync(
            new CopyTaskRequest(
                workspace.SourceDirectory,
                workspace.SourceDirectory,
                CopyMode.AccumulateSync)));
}

static async Task ExecutorCopiesContentAndTimestamp()
{
    using var workspace = TestWorkspace.Create();
    var sourceTime = new DateTime(2026, 8, 12, 10, 30, 0, DateTimeKind.Utc);
    workspace.WriteSource(Path.Combine("nested", "payload.txt"), "copy-tools-payload", sourceTime);
    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);

    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.True(result.IsSuccessful);
    Assert.Equal(1, result.SuccessfulFiles);
    Assert.Equal("copy-tools-payload", workspace.ReadTarget(Path.Combine("nested", "payload.txt")));
    var actualTime = File.GetLastWriteTimeUtc(Path.Combine(workspace.TargetDirectory, "nested", "payload.txt"));
    Assert.True(Math.Abs((actualTime - sourceTime).TotalSeconds) <= 2, "目标文件修改时间未保留。");
    Assert.False(Directory.EnumerateFiles(workspace.TargetDirectory, "*.tmp", SearchOption.AllDirectories).Any());
}

static async Task ReplanAfterExecutionSkipsUnchangedFiles()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("data.bin", new string('x', 8192));
    var service = new CopyPlanService();
    var request = workspace.Request(CopyMode.AccumulateSync);
    var initialPlan = await service.CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(initialPlan);
    var secondPlan = await service.CreatePlanAsync(request);

    Assert.True(result.IsSuccessful);
    Assert.Equal(0, secondPlan.FilesToCopy);
    Assert.Equal(CopyAction.SkipUnchanged, secondPlan.Items.Single().Action);
}

static async Task RejectsSourceChangedAfterPreview()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("changing.txt", "before");
    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);
    workspace.WriteSource("changing.txt", "after-preview-with-different-size", DateTime.UtcNow.AddMinutes(1));

    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.False(result.IsSuccessful);
    Assert.Equal(1, result.Failures.Count);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "changing.txt")));
}

static async Task RejectsTargetAppearingAfterPreview()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("appeared.txt", "source");
    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);
    workspace.WriteTarget("appeared.txt", "created-after-preview");

    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.False(result.IsSuccessful);
    Assert.Equal(1, result.Failures.Count);
    Assert.Equal("created-after-preview", workspace.ReadTarget("appeared.txt"));
}

static async Task FiltersSelectExpectedFiles()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("keep.txt", "12345");
    workspace.WriteSource("skip.log", "12345");
    workspace.WriteSource(Path.Combine("cache", "hidden.txt"), "12345");
    workspace.WriteSource("tiny.txt", "x");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.AccumulateSync,
        new FileFilterOptions(
            IncludePatterns: ["*.txt"],
            ExcludedDirectories: ["cache"],
            MinimumBytes: 5));

    var plan = await new CopyPlanService().CreatePlanAsync(request);

    Assert.Equal(1, plan.TotalFiles);
    Assert.Equal("keep.txt", plan.Items.Single().RelativePath);
}

static async Task Sha256VerificationCopiesFile()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("strict.bin", new string('s', 32_768));
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.AccumulateSync,
        VerificationMode: VerificationMode.Sha256,
        RetryPolicy: new RetryPolicy(1, TimeSpan.Zero));
    var plan = await new CopyPlanService().CreatePlanAsync(request);

    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.True(result.IsSuccessful);
    Assert.Equal(new string('s', 32_768), workspace.ReadTarget("strict.bin"));
}

static async Task Sha256DetectsSameMetadataContentChange()
{
    using var workspace = TestWorkspace.Create();
    var timestamp = DateTime.UtcNow.AddMinutes(-5);
    workspace.WriteSource("same-metadata.bin", "AAAA", timestamp);
    workspace.WriteTarget("same-metadata.bin", "BBBB", timestamp);
    var quick = await new CopyPlanService().CreatePlanAsync(new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync));
    var strict = await new CopyPlanService().CreatePlanAsync(new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync,
        VerificationMode: VerificationMode.Sha256));
    Assert.Equal(0, quick.FilesToOverwrite);
    Assert.Equal(1, strict.FilesToOverwrite);
    Assert.Equal(CopyAction.Overwrite, strict.Items.Single().Action);
}

static async Task SqliteTaskRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var databasePath = Path.Combine(workspace.RootDirectory, "任务数据.db");
    var repository = new SqliteCopyToolsRepository(databasePath);
    await repository.InitializeAsync();
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(
        Guid.NewGuid(),
        "中文同步任务",
        new CopyTaskRequest(
            workspace.SourceDirectory,
            workspace.TargetDirectory,
            CopyMode.AccumulateSync,
            new FileFilterOptions(IncludePatterns: ["*.文档", "*.txt"]),
            VerificationMode.Sha256,
            new RetryPolicy(4, TimeSpan.FromMilliseconds(250))),
        now,
        now);

    await repository.SaveTaskAsync(task);
    var loaded = await repository.GetTaskAsync(task.Id);

    Assert.NotNull(loaded);
    Assert.Equal(task.Name, loaded!.Name);
    Assert.Equal(task.Request.Mode, loaded.Request.Mode);
    Assert.Equal(VerificationMode.Sha256, loaded.Request.VerificationMode);
    Assert.Equal(4, loaded.Request.EffectiveRetryPolicy.MaxAttempts);
    Assert.Equal("*.文档", loaded.Request.EffectiveFilters.EffectiveIncludePatterns[0]);
}

static async Task SqliteRunHistoryRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "history.db"));
    await repository.InitializeAsync();
    var runId = Guid.NewGuid();
    var request = workspace.Request(CopyMode.AccumulateSync);
    var run = new RunHistoryEntry(
        runId,
        null,
        "临时任务",
        request,
        DateTime.UtcNow.AddSeconds(-2),
        DateTime.UtcNow,
        "部分成功",
        1,
        1,
        1,
        128,
        [
            new RunHistoryItem("成功.txt", CopyAction.CopyNew, "成功", 128, null, 1),
            new RunHistoryItem("失败.txt", CopyAction.Overwrite, "失败", 0, "访问被拒绝", 3),
            new RunHistoryItem("跳过.txt", CopyAction.SkipUnchanged, "跳过", 0, null, 0),
        ]);

    await repository.SaveRunAsync(run);
    var loaded = await repository.GetRunAsync(runId);
    var recent = await repository.GetRecentRunsAsync();

    Assert.NotNull(loaded);
    Assert.Equal(3, loaded!.Items.Count);
    Assert.Equal("访问被拒绝", loaded.Items[1].ErrorMessage);
    Assert.Equal(3, loaded.Items[1].Attempts);
    Assert.Equal(runId, recent.Single().Id);
}

static async Task MirrorSyncQuarantinesExtraTarget()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("keep.txt", "keep");
    workspace.WriteTarget("extra.txt", "extra");
    var quarantineRoot = Path.Combine(workspace.RootDirectory, "quarantine");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.MirrorSync,
        DeleteSafetyPolicy: new DeleteSafetyPolicy(100, 100, quarantineRoot));

    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.Equal(1, plan.FilesToQuarantine);
    Assert.True(result.IsSuccessful);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "extra.txt")));
    Assert.Equal(1, result.EffectiveQuarantinedFiles.Count);
    Assert.True(File.Exists(result.EffectiveQuarantinedFiles[0].QuarantinePath));
    Assert.Equal("keep", workspace.ReadTarget("keep.txt"));
}

static async Task MirrorSyncDeleteThresholdBlocksPlan()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteTarget("important.txt", "must-not-delete");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.MirrorSync,
        DeleteSafetyPolicy: new DeleteSafetyPolicy(100, 25, Path.Combine(workspace.RootDirectory, "q")));

    await Assert.ThrowsAsync<CopyTaskValidationException>(() =>
        new CopyPlanService().CreatePlanAsync(request));

    Assert.Equal("must-not-delete", workspace.ReadTarget("important.txt"));
}

static async Task MoveSyncQuarantinesSourceAfterCopy()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("move.txt", "move-me");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.MoveSync,
        VerificationMode: VerificationMode.Sha256,
        DeleteSafetyPolicy: new DeleteSafetyPolicy(100, 100, Path.Combine(workspace.RootDirectory, "q")));

    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.True(result.IsSuccessful);
    Assert.False(File.Exists(Path.Combine(workspace.SourceDirectory, "move.txt")));
    Assert.Equal("move-me", workspace.ReadTarget("move.txt"));
    Assert.Equal(1, result.EffectiveQuarantinedFiles.Count);
    Assert.True(File.Exists(result.EffectiveQuarantinedFiles.Single().QuarantinePath));
}

static async Task MoveSyncRetainsSourceWhenCopyFails()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("blocked.txt", "source-must-survive");
    Directory.CreateDirectory(Path.Combine(workspace.TargetDirectory, "blocked.txt"));
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.MoveSync,
        RetryPolicy: new RetryPolicy(1, TimeSpan.Zero),
        DeleteSafetyPolicy: new DeleteSafetyPolicy(100, 100, Path.Combine(workspace.RootDirectory, "q")));

    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.False(result.IsSuccessful);
    Assert.True(File.Exists(Path.Combine(workspace.SourceDirectory, "blocked.txt")));
    Assert.Equal("source-must-survive", File.ReadAllText(Path.Combine(workspace.SourceDirectory, "blocked.txt"), Encoding.UTF8));
}

static async Task QuarantineRestoreRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteTarget("restore.txt", "restore-me");
    var quarantineRoot = Path.Combine(workspace.RootDirectory, "q");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.MirrorSync,
        DeleteSafetyPolicy: new DeleteSafetyPolicy(100, 100, quarantineRoot));
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "restore.db"));
    await repository.InitializeAsync();
    var runId = Guid.NewGuid();
    var quarantineEntries = result.EffectiveQuarantinedFiles
        .Select(file => new QuarantineEntry(
            Guid.NewGuid(),
            runId,
            file.OriginalPath,
            file.QuarantinePath,
            file.RelativePath,
            file.Length,
            file.QuarantinedAtUtc,
            null))
        .ToArray();
    var run = new RunHistoryEntry(
        runId,
        null,
        "restore-test",
        request,
        result.StartedAtUtc,
        result.CompletedAtUtc,
        result.Status,
        result.SuccessfulFiles,
        result.SkippedFiles,
        result.Failures.Count,
        result.CopiedBytes,
        [],
        quarantineEntries);
    await repository.SaveRunAsync(run);

    var workflow = new CopyTools.Application.QuarantineWorkflow(repository);
    var entry = (await workflow.GetEntriesAsync()).Single();
    await workflow.RestoreAsync(entry, overwrite: false);
    var activeEntries = await workflow.GetEntriesAsync();
    var allEntries = await workflow.GetEntriesAsync(includeRestored: true);

    Assert.Equal("restore-me", workspace.ReadTarget("restore.txt"));
    Assert.Equal(0, activeEntries.Count);
    Assert.NotNull(allEntries.Single().RestoredAtUtc);
}

static async Task SyncSnapshotRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "snapshot.db"));
    await repository.InitializeAsync();
    var synchronizationId = Guid.NewGuid();
    var timestamp = new DateTime(2026, 8, 13, 6, 30, 0, DateTimeKind.Utc);
    var states = new[]
    {
        new SyncFileState("中文/资料.txt", true, 123, timestamp, false, null, null,
            SourceContentHash: "a1b2c3", TargetContentHash: null),
        new SyncFileState("target-only.bin", false, null, null, true, 456, timestamp.AddMinutes(1),
            SourceContentHash: null, TargetContentHash: "d4e5f6"),
    };

    await repository.ReplaceSnapshotAsync(synchronizationId, states);
    var loaded = await repository.GetSnapshotAsync(synchronizationId);

    Assert.Equal(2, loaded.Count);
    Assert.Equal("target-only.bin", loaded[0].RelativePath);
    Assert.Equal("中文/资料.txt", loaded[1].RelativePath);
    Assert.Equal(123L, loaded[1].SourceLength);
    Assert.Equal(timestamp, loaded[1].SourceLastWriteTimeUtc);
    Assert.Equal("a1b2c3", loaded[1].SourceContentHash);
    Assert.Equal("d4e5f6", loaded[0].TargetContentHash);
}

static async Task UpdateSyncPublishesChangedBatch()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.txt", "a-v1");
    workspace.WriteSource("b.txt", "b-v1");
    var repository = await CreateRepository(workspace, "update.db");
    var workflow = CreateSyncWorkflow(repository);
    var request = SyncRequest(workspace, CopyMode.UpdateSync, Guid.NewGuid());

    var firstResult = await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request));
    Assert.True(firstResult.IsSuccessful);
    Assert.Equal("a-v1", workspace.ReadTarget("a.txt"));
    Assert.Equal("b-v1", workspace.ReadTarget("b.txt"));

    workspace.WriteSource("a.txt", "a-version-two-with-new-size", DateTime.UtcNow.AddMinutes(1));
    workspace.WriteSource("c.txt", "c-v1");
    var secondPlan = await workflow.CreatePlanAsync(request);

    Assert.Equal(2, secondPlan.FilesToCopy);
    Assert.Equal(1, secondPlan.TargetFilesToQuarantine);
    Assert.Equal("b.txt", secondPlan.Items.Single(item => item.Action == CopyAction.QuarantineTarget).RelativePath);

    var secondResult = await workflow.ExecuteAsync(secondPlan);
    Assert.True(secondResult.IsSuccessful);
    Assert.Equal("a-version-two-with-new-size", workspace.ReadTarget("a.txt"));
    Assert.Equal("c-v1", workspace.ReadTarget("c.txt"));
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "b.txt")));
}

static async Task OneWaySyncPropagatesManagedDeletionOnly()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("managed.txt", "managed-v1");
    workspace.WriteTarget("target-only.txt", "keep-me");
    var repository = await CreateRepository(workspace, "one-way.db");
    var workflow = CreateSyncWorkflow(repository);
    var request = SyncRequest(workspace, CopyMode.OneWaySync, Guid.NewGuid());
    var first = await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request));
    Assert.True(first.IsSuccessful);
    Assert.Equal("keep-me", workspace.ReadTarget("target-only.txt"));

    File.Delete(Path.Combine(workspace.SourceDirectory, "managed.txt"));
    workspace.WriteSource("new.txt", "new-value");
    var plan = await workflow.CreatePlanAsync(request);
    Assert.Equal(1, plan.TargetFilesToQuarantine);
    Assert.False(plan.Items.Any(item => item.RelativePath == "target-only.txt" && item.WillExecute));
    var second = await workflow.ExecuteAsync(plan);
    Assert.True(second.IsSuccessful);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "managed.txt")));
    Assert.Equal("keep-me", workspace.ReadTarget("target-only.txt"));
    Assert.Equal("new-value", workspace.ReadTarget("new.txt"));
}

static async Task UpdateSyncCommitsEmptyPlan()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("same.txt", "same");
    var repository = await CreateRepository(workspace, "update-empty.db");
    var workflow = CreateSyncWorkflow(repository);
    var synchronizationId = Guid.NewGuid();
    var request = SyncRequest(workspace, CopyMode.UpdateSync, synchronizationId);
    Assert.True((await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request))).IsSuccessful);

    var emptyPlan = await workflow.CreatePlanAsync(request);
    var emptyResult = await workflow.ExecuteAsync(emptyPlan);
    var snapshot = await repository.GetSnapshotAsync(synchronizationId);

    Assert.Equal(0, emptyPlan.OperationsToExecute);
    Assert.True(emptyResult.IsSuccessful);
    Assert.Equal(1, snapshot.Count);
}

static async Task TwoWaySyncPropagatesTargetChange()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("shared.txt", "initial");
    var repository = await CreateRepository(workspace, "two-way-target.db");
    var workflow = CreateSyncWorkflow(repository);
    var request = SyncRequest(workspace, CopyMode.TwoWaySync, Guid.NewGuid());
    Assert.True((await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request))).IsSuccessful);

    workspace.WriteTarget("shared.txt", "changed-from-target-with-different-size", DateTime.UtcNow.AddMinutes(1));
    var plan = await workflow.CreatePlanAsync(request);
    var result = await workflow.ExecuteAsync(plan);

    Assert.Equal(1, plan.FilesToOverwrite);
    Assert.True(result.IsSuccessful);
    Assert.Equal("changed-from-target-with-different-size",
        File.ReadAllText(Path.Combine(workspace.SourceDirectory, "shared.txt"), Encoding.UTF8));
}

static async Task TwoWaySyncPropagatesDeletionSafely()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("delete-me.txt", "payload");
    var repository = await CreateRepository(workspace, "two-way-delete.db");
    var workflow = CreateSyncWorkflow(repository);
    var request = SyncRequest(workspace, CopyMode.TwoWaySync, Guid.NewGuid());
    Assert.True((await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request))).IsSuccessful);

    File.Delete(Path.Combine(workspace.SourceDirectory, "delete-me.txt"));
    var plan = await workflow.CreatePlanAsync(request);
    var result = await workflow.ExecuteAsync(plan);

    Assert.Equal(1, plan.TargetFilesToQuarantine);
    Assert.True(result.IsSuccessful);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "delete-me.txt")));
    Assert.True(File.Exists(result.EffectiveQuarantinedFiles.Single().QuarantinePath));
}

static async Task TwoWaySyncPreservesConflictCopies()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("conflict.txt", "initial");
    var repository = await CreateRepository(workspace, "two-way-conflict.db");
    var workflow = CreateSyncWorkflow(repository);
    var request = SyncRequest(workspace, CopyMode.TwoWaySync, Guid.NewGuid());
    Assert.True((await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request))).IsSuccessful);

    workspace.WriteSource("conflict.txt", "source-side-change", DateTime.UtcNow.AddMinutes(1));
    workspace.WriteTarget("conflict.txt", "target-side-change-with-different-size", DateTime.UtcNow.AddMinutes(2));
    var plan = await workflow.CreatePlanAsync(request);
    var result = await workflow.ExecuteAsync(plan);

    Assert.Equal(1, plan.Conflicts);
    Assert.Equal(2, plan.Items.Count(item => item.Action == CopyAction.ConflictCopy));
    Assert.True(result.IsSuccessful);
    Assert.Equal("source-side-change", File.ReadAllText(Path.Combine(workspace.SourceDirectory, "conflict.txt"), Encoding.UTF8));
    Assert.Equal("target-side-change-with-different-size", workspace.ReadTarget("conflict.txt"));
    Assert.Equal(1, Directory.EnumerateFiles(workspace.SourceDirectory, "*.conflict-target-*", SearchOption.AllDirectories).Count());
    Assert.Equal(1, Directory.EnumerateFiles(workspace.TargetDirectory, "*.conflict-source-*", SearchOption.AllDirectories).Count());
}

static async Task FailedSyncDoesNotAdvanceSnapshot()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("changing.txt", "before");
    var repository = await CreateRepository(workspace, "failed-snapshot.db");
    var workflow = CreateSyncWorkflow(repository);
    var synchronizationId = Guid.NewGuid();
    var request = SyncRequest(workspace, CopyMode.TwoWaySync, synchronizationId);
    var plan = await workflow.CreatePlanAsync(request);
    workspace.WriteSource("changing.txt", "changed-after-preview-with-new-size", DateTime.UtcNow.AddMinutes(1));

    var result = await workflow.ExecuteAsync(plan);
    var snapshot = await repository.GetSnapshotAsync(synchronizationId);

    Assert.False(result.IsSuccessful);
    Assert.Equal(0, snapshot.Count);
}

static async Task<SqliteCopyToolsRepository> CreateRepository(TestWorkspace workspace, string name)
{
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, name));
    await repository.InitializeAsync();
    return repository;
}

static CopyWorkflow CreateSyncWorkflow(SqliteCopyToolsRepository repository) =>
    new(new CopyPlanService(snapshotStore: repository));

static CopyTaskRequest SyncRequest(TestWorkspace workspace, CopyMode mode, Guid synchronizationId) =>
    new(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        mode,
        VerificationMode: VerificationMode.Sha256,
        RetryPolicy: new RetryPolicy(1, TimeSpan.Zero),
        DeleteSafetyPolicy: new DeleteSafetyPolicy(
            10_000,
            100,
            Path.Combine(workspace.RootDirectory, "quarantine")),
        SynchronizationId: synchronizationId);

static Task ScheduleCalculatorSupportsTriggers()
{
    var calculator = new ScheduleCalculator();
    var utc = TimeZoneInfo.Utc;
    var monday = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
    var daily = new TaskSchedule(Guid.NewGuid(), true, ScheduleTrigger.Daily, TimeSpan.FromHours(11));
    var weekly = new TaskSchedule(
        Guid.NewGuid(), true, ScheduleTrigger.Weekly, TimeSpan.FromHours(9), DayOfWeek.Wednesday);
    var monthly = new TaskSchedule(
        Guid.NewGuid(), true, ScheduleTrigger.Monthly, TimeSpan.FromHours(8), DayOfMonth: 31);
    var interval = new TaskSchedule(
        Guid.NewGuid(), true, ScheduleTrigger.Interval, TimeSpan.Zero,
        Interval: TimeSpan.FromMinutes(30), LastTriggeredAtUtc: monday);

    Assert.Equal(new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc),
        calculator.CalculateNextUtc(daily, monday, utc));
    Assert.Equal(new DateTime(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc),
        calculator.CalculateNextUtc(weekly, monday, utc));
    Assert.Equal(new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Utc),
        calculator.CalculateNextUtc(monthly, monday, utc));
    Assert.Equal(monday.AddMinutes(30), calculator.CalculateNextUtc(interval, monday, utc));
    return Task.CompletedTask;
}

static async Task ScheduleRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "schedule.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "计划任务", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    var schedule = new TaskSchedule(
        task.Id,
        true,
        ScheduleTrigger.Weekly,
        TimeSpan.FromHours(22.5),
        DayOfWeek.Friday,
        MissedRunPolicy: MissedRunPolicy.Skip,
        AllowOverwrite: true,
        NextRunAtUtc: now.AddDays(2));

    await repository.SaveScheduleAsync(schedule);
    var loaded = await repository.GetScheduleAsync(task.Id);

    Assert.NotNull(loaded);
    Assert.Equal(ScheduleTrigger.Weekly, loaded!.Trigger);
    Assert.Equal(TimeSpan.FromHours(22.5), loaded.LocalTime);
    Assert.Equal(DayOfWeek.Friday, loaded.DayOfWeek);
    Assert.Equal(MissedRunPolicy.Skip, loaded.MissedRunPolicy);
    Assert.True(loaded.AllowOverwrite);
}

static async Task ScheduledRunBlocksUnapprovedOverwrite()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("overwrite.txt", "source-new-content");
    workspace.WriteTarget("overwrite.txt", "old");
    var repository = await CreateRepository(workspace, "schedule-block.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "blocked", workspace.Request(CopyMode.FullCopy), now, now);
    await repository.SaveTaskAsync(task);
    var runner = new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository));

    var result = await runner.RunAsync(new TaskSchedule(
        task.Id, true, ScheduleTrigger.Daily, TimeSpan.Zero));
    var history = await repository.GetRecentRunsAsync();

    Assert.False(result.Executed);
    Assert.Equal("等待人工确认", result.Status);
    Assert.Equal("old", workspace.ReadTarget("overwrite.txt"));
    Assert.Equal("等待人工确认", history.Single().Status);
}

static async Task ScheduledRunExecutesAndSavesHistory()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("new.txt", "payload");
    var repository = await CreateRepository(workspace, "schedule-run.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "auto-run", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    var runner = new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository));

    var result = await runner.RunAsync(new TaskSchedule(
        task.Id, true, ScheduleTrigger.Interval, TimeSpan.Zero, Interval: TimeSpan.FromMinutes(5)));
    var history = await repository.GetRecentRunsAsync();

    Assert.True(result.Executed);
    Assert.Equal("payload", workspace.ReadTarget("new.txt"));
    Assert.Equal("auto-run", history.Single().TaskName);
    Assert.Equal(1, history.Single().SuccessfulFiles);
}

static async Task MissedScheduleSkipDoesNotExecute()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("skip.txt", "payload");
    var repository = await CreateRepository(workspace, "schedule-skip.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "skip-run", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    await repository.SaveScheduleAsync(new TaskSchedule(
        task.Id,
        true,
        ScheduleTrigger.Daily,
        TimeSpan.Zero,
        MissedRunPolicy: MissedRunPolicy.Skip,
        NextRunAtUtc: now.AddMinutes(-5)));
    var scheduler = new TaskSchedulerService(
        repository,
        new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository)));

    var results = await scheduler.ProcessDueAsync(now);
    var updated = await repository.GetScheduleAsync(task.Id);

    Assert.Equal(0, results.Count);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "skip.txt")));
    Assert.NotNull(updated);
    Assert.True(updated!.NextRunAtUtc > now);
}

static async Task VerifyOnlyDetectsMismatchWithoutWriting()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("verify.txt", "source-content");
    workspace.WriteTarget("verify.txt", "target-content");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.VerifyOnly,
        VerificationMode: VerificationMode.Sha256,
        RetryPolicy: new RetryPolicy(1, TimeSpan.Zero));
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.Equal(1, plan.FilesToVerify);
    Assert.False(result.IsSuccessful);
    Assert.Equal("target-content", workspace.ReadTarget("verify.txt"));
    Assert.True(result.Failures.Single().ErrorMessage.Contains("SHA-256", StringComparison.Ordinal));
}

static async Task VerifyOnlyReportsMissingTarget()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("missing.txt", "source");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.VerifyOnly,
        RetryPolicy: new RetryPolicy(1, TimeSpan.Zero));
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.Equal(CopyAction.Verify, plan.Items.Single().Action);
    Assert.False(result.IsSuccessful);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "missing.txt")));
}

static async Task CopyPreservesFileAttributes()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("attributes.txt", "payload");
    var sourcePath = Path.Combine(workspace.SourceDirectory, "attributes.txt");
    File.SetAttributes(sourcePath, FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.Archive);
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.AccumulateSync,
        new FileFilterOptions(IncludeHiddenFiles: true));

    var result = await new CopyExecutor().ExecuteAsync(await new CopyPlanService().CreatePlanAsync(request));
    var targetAttributes = File.GetAttributes(Path.Combine(workspace.TargetDirectory, "attributes.txt"));

    Assert.True(result.IsSuccessful);
    Assert.True(targetAttributes.HasFlag(FileAttributes.Hidden));
    Assert.True(targetAttributes.HasFlag(FileAttributes.ReadOnly));
    File.SetAttributes(sourcePath, FileAttributes.Normal);
    File.SetAttributes(Path.Combine(workspace.TargetDirectory, "attributes.txt"), FileAttributes.Normal);
}

static async Task PauseAndResumeControlsCopy()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("paused.bin", new string('p', 2_500_000));
    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);
    var control = new CopyExecutionControl();
    control.Pause();
    var execution = new CopyExecutor().ExecuteAsync(plan, executionControl: control);
    await Task.Delay(100);
    Assert.False(execution.IsCompleted);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "paused.bin")));

    control.Resume();
    var result = await execution;
    Assert.True(result.IsSuccessful);
    Assert.Equal(2_500_000, workspace.ReadTarget("paused.bin").Length);
}

static async Task FailedItemsCanBeRetried()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("retry.txt", "payload");
    var workflow = new CopyWorkflow();
    var plan = await workflow.CreatePlanAsync(workspace.Request(CopyMode.AccumulateSync));
    Directory.CreateDirectory(Path.Combine(workspace.TargetDirectory, "retry.txt"));
    var first = await workflow.ExecuteAsync(plan);
    Directory.Delete(Path.Combine(workspace.TargetDirectory, "retry.txt"));

    var retry = await workflow.RetryFailuresAsync(plan, first);

    Assert.False(first.IsSuccessful);
    Assert.True(retry.IsSuccessful);
    Assert.Equal("payload", workspace.ReadTarget("retry.txt"));
}

static async Task SyncPreservesEmptyDirectories()
{
    using var workspace = TestWorkspace.Create();
    Directory.CreateDirectory(Path.Combine(workspace.SourceDirectory, "empty", "nested"));
    var workflow = new CopyWorkflow();
    var plan = await workflow.CreatePlanAsync(workspace.Request(CopyMode.AccumulateSync));
    var result = await workflow.ExecuteAsync(plan);

    Assert.Equal(1, plan.DirectoriesToCreate);
    Assert.True(result.IsSuccessful);
    Assert.True(Directory.Exists(Path.Combine(workspace.TargetDirectory, "empty", "nested")));
}

static async Task ExistingFileCanBeRenamed()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("same.txt", "source");
    workspace.WriteTarget("same.txt", "target");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.FullCopy,
        ExistingFilePolicy: ExistingFilePolicy.Rename);
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.Equal(CopyAction.RenameCopy, plan.Items.Single().Action);
    Assert.True(result.IsSuccessful);
    Assert.Equal("target", workspace.ReadTarget("same.txt"));
    Assert.Equal("source", workspace.ReadTarget("same (1).txt"));
}

static async Task ExistingFileCanBeSkippedByPolicy()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("same.txt", "source");
    workspace.WriteTarget("same.txt", "target");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.FullCopy,
        ExistingFilePolicy: ExistingFilePolicy.Skip);
    var plan = await new CopyPlanService().CreatePlanAsync(request);

    Assert.Equal(CopyAction.SkipExisting, plan.Items.Single().Action);
    Assert.Equal("target", workspace.ReadTarget("same.txt"));
}

static async Task PlanCanExcludeSelectedItems()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.txt", "a");
    workspace.WriteSource("b.txt", "b");
    var plan = await workspace.CreatePlan(CopyMode.AccumulateSync);
    var edited = PlanEditor.ExcludeItems(plan, [0]);
    var result = await new CopyExecutor().ExecuteAsync(edited);

    Assert.Equal(1, edited.Items.Count);
    Assert.True(result.IsSuccessful);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "a.txt")));
    Assert.Equal("b", workspace.ReadTarget("b.txt"));
}

static async Task TaskBundleExportImportRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var sourceRepository = await CreateRepository(workspace, "transfer-source.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(
        Guid.NewGuid(),
        "导出任务",
        new CopyTaskRequest(
            workspace.SourceDirectory,
            workspace.TargetDirectory,
            CopyMode.TwoWaySync,
            SynchronizationId: Guid.NewGuid()),
        now,
        now);
    await sourceRepository.SaveTaskAsync(task);
    await sourceRepository.SaveScheduleAsync(new TaskSchedule(
        task.Id, true, ScheduleTrigger.Daily, TimeSpan.FromHours(3)));
    var bundlePath = Path.Combine(workspace.RootDirectory, "tasks.copytools.json");
    await new TaskTransferService(sourceRepository).ExportAsync(bundlePath);

    var targetRepository = await CreateRepository(workspace, "transfer-target.db");
    var count = await new TaskTransferService(targetRepository).ImportAsync(bundlePath);
    var imported = (await targetRepository.GetTasksAsync()).Single();
    var schedule = await targetRepository.GetScheduleAsync(imported.Id);

    Assert.Equal(1, count);
    Assert.Equal("导出任务", imported.Name);
    Assert.Equal(imported.Id, imported.Request.SynchronizationId);
    Assert.NotNull(schedule);
}

static async Task RunLogsExportInThreeFormats()
{
    using var workspace = TestWorkspace.Create();
    var now = DateTime.UtcNow;
    var run = new RunHistoryEntry(
        Guid.NewGuid(),
        null,
        "日志任务",
        workspace.Request(CopyMode.AccumulateSync),
        now.AddSeconds(-1),
        now,
        "部分成功",
        1,
        0,
        1,
        12,
        [new RunHistoryItem("带,逗号.txt", CopyAction.CopyNew, "失败", 0, "错误\"详情", 3)]);
    var exporter = new RunLogExportService();
    var json = Path.Combine(workspace.RootDirectory, "run.json");
    var csv = Path.Combine(workspace.RootDirectory, "run.csv");
    var text = Path.Combine(workspace.RootDirectory, "run.txt");

    await exporter.ExportJsonAsync(run, json);
    await exporter.ExportCsvAsync(run, csv);
    await exporter.ExportTextAsync(run, text);

    Assert.True(File.ReadAllText(json).Contains("日志任务", StringComparison.Ordinal));
    Assert.True(File.ReadAllText(csv).Contains("\"带,逗号.txt\"", StringComparison.Ordinal));
    Assert.True(File.ReadAllText(text).Contains("部分成功", StringComparison.Ordinal));
}

static async Task IncrementalBackupReusesObjectsAndRestoresSnapshot()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.txt", "a-v1");
    workspace.WriteSource("b.txt", "b-v1");
    var repository = Path.Combine(workspace.RootDirectory, "backup-repository");
    var service = new BackupRepositoryService();
    var request = new BackupTaskRequest(
        workspace.SourceDirectory, repository, BackupMode.IncrementalHistory);

    var first = await service.CreateBackupAsync(request);
    workspace.WriteSource("a.txt", "a-v2-with-different-size", DateTime.UtcNow.AddMinutes(1));
    var second = await service.CreateBackupAsync(request);
    var restore = Path.Combine(workspace.RootDirectory, "restore-first");
    await service.RestoreAsync(repository, first.Snapshot, restore);

    Assert.Equal(2, first.NewObjects);
    Assert.Equal(1, second.NewObjects);
    Assert.Equal(1, second.ReusedObjects);
    Assert.Equal("a-v1", File.ReadAllText(Path.Combine(restore, "a.txt"), Encoding.UTF8));
    Assert.Equal("b-v1", File.ReadAllText(Path.Combine(restore, "b.txt"), Encoding.UTF8));
}

static async Task FullBackupCreatesIndependentObjects()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("same.txt", "same");
    var repository = Path.Combine(workspace.RootDirectory, "full-repository");
    var service = new BackupRepositoryService();
    var request = new BackupTaskRequest(workspace.SourceDirectory, repository, BackupMode.FullSnapshot);

    var first = await service.CreateBackupAsync(request);
    var second = await service.CreateBackupAsync(request);

    Assert.Equal(1, first.NewObjects);
    Assert.Equal(1, second.NewObjects);
    Assert.False(string.Equals(
        first.Snapshot.Files.Single().ObjectRelativePath,
        second.Snapshot.Files.Single().ObjectRelativePath,
        StringComparison.OrdinalIgnoreCase));
}

static async Task BackupRestoresSelectedFiles()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.txt", "a");
    workspace.WriteSource("b.txt", "b");
    var repository = Path.Combine(workspace.RootDirectory, "selective-repository");
    var service = new BackupRepositoryService();
    var backup = await service.CreateBackupAsync(new BackupTaskRequest(
        workspace.SourceDirectory, repository, BackupMode.IncrementalHistory));
    var restore = Path.Combine(workspace.RootDirectory, "selective-restore");

    await service.RestoreAsync(repository, backup.Snapshot, restore, ["b.txt"]);

    Assert.False(File.Exists(Path.Combine(restore, "a.txt")));
    Assert.Equal("b", File.ReadAllText(Path.Combine(restore, "b.txt"), Encoding.UTF8));
}

static async Task BackupRetentionPreservesReferencedObjects()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("stable.txt", "stable");
    workspace.WriteSource("changing.txt", "version-one");
    var repository = Path.Combine(workspace.RootDirectory, "retention-repository");
    var service = new BackupRepositoryService();
    var request = new BackupTaskRequest(
        workspace.SourceDirectory, repository, BackupMode.IncrementalHistory);
    var first = await service.CreateBackupAsync(request);
    workspace.WriteSource("changing.txt", "version-two-longer", DateTime.UtcNow.AddMinutes(1));
    var second = await service.CreateBackupAsync(request);

    var deleted = await service.ApplyRetentionAsync(repository, keepLatest: 1);
    var snapshots = await service.GetSnapshotsAsync(repository);
    var restore = Path.Combine(workspace.RootDirectory, "retention-restore");
    await service.RestoreAsync(repository, snapshots.Single(), restore);

    Assert.Equal(1, deleted);
    Assert.Equal(second.Snapshot.Id, snapshots.Single().Id);
    Assert.Equal("stable", File.ReadAllText(Path.Combine(restore, "stable.txt"), Encoding.UTF8));
    Assert.False(File.Exists(Path.Combine(repository, first.Snapshot.Files.Single(file => file.RelativePath == "changing.txt").ObjectRelativePath)));
}

static async Task BackupPreservesAttributesWithoutLockingObjects()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("readonly.txt", "payload");
    var sourcePath = Path.Combine(workspace.SourceDirectory, "readonly.txt");
    File.SetAttributes(sourcePath, FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive);
    var repository = Path.Combine(workspace.RootDirectory, "attribute-repository");
    var service = new BackupRepositoryService();
    var backup = await service.CreateBackupAsync(new BackupTaskRequest(
        workspace.SourceDirectory,
        repository,
        BackupMode.IncrementalHistory,
        new FileFilterOptions(IncludeHiddenFiles: true)));
    var objectPath = Path.Combine(repository, backup.Snapshot.Files.Single().ObjectRelativePath);
    var restore = Path.Combine(workspace.RootDirectory, "attribute-restore");
    await service.RestoreAsync(repository, backup.Snapshot, restore);
    var restored = Path.Combine(restore, "readonly.txt");

    Assert.False(File.GetAttributes(objectPath).HasFlag(FileAttributes.ReadOnly));
    Assert.True(File.GetAttributes(restored).HasFlag(FileAttributes.ReadOnly));
    Assert.True(File.GetAttributes(restored).HasFlag(FileAttributes.Hidden));
    File.SetAttributes(sourcePath, FileAttributes.Normal);
    File.SetAttributes(restored, FileAttributes.Normal);
}

static Task PerformanceOptionsNormalizeSafely()
{
    var normalized = new PerformanceOptions(1, 1, 99).Normalize();
    Assert.Equal(64 * 1024, normalized.BufferSizeBytes);
    Assert.Equal(64L * 1024, normalized.SpeedLimitBytesPerSecond);
    Assert.Equal(16, normalized.MaxParallelFiles);
    return Task.CompletedTask;
}

static Task CopyPerformanceAutoTuneUsesSafeLimits()
{
    using var workspace = TestWorkspace.Create();
    var request = new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory,
        CopyMode.AccumulateSync, Performance: new PerformanceOptions(1024 * 1024, MaxParallelFiles: 8));
    var few = Enumerable.Range(0, 5).Select(index => new CopyPlanItem($"{index}.txt", "s", "t", 10,
        DateTime.UtcNow, CopyAction.CopyNew, "test")).ToArray();
    var resolvedFew = CopyPerformanceTuner.Resolve(new CopyPlan(request, few, DateTime.UtcNow));
    Assert.Equal(1, resolvedFew.MaxParallelFiles);
    var manual = CopyPerformanceTuner.Resolve(new CopyPlan(
        request with { Performance = request.EffectivePerformance with { AutoTune = false } }, few, DateTime.UtcNow));
    Assert.Equal(8, manual.MaxParallelFiles);
    return Task.CompletedTask;
}

static Task BackgroundPerformancePolicyLowersImpact()
{
    using var workspace = TestWorkspace.Create();
    var request = new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory,
        CopyMode.AccumulateSync,
        Performance: new PerformanceOptions(1024 * 1024, MaxParallelFiles: 8, BackgroundLowImpact: true));
    var items = Enumerable.Range(0, 20).Select(index => new CopyPlanItem($"{index}.bin", "s", "t", 10,
        DateTime.UtcNow, CopyAction.CopyNew, "test")).ToArray();
    var resolved = CopyPerformanceTuner.Resolve(new CopyPlan(request, items, DateTime.UtcNow));
    Assert.Equal(1, resolved.MaxParallelFiles);
    Assert.Equal(64L * 1024 * 1024, resolved.SpeedLimitBytesPerSecond);
    return Task.CompletedTask;
}

static async Task SpeedLimitThrottlesCopy()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("limited.bin", new string('x', 300_000));
    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.AccumulateSync,
        Performance: new PerformanceOptions(64 * 1024, 600_000));
    var workflow = new CopyWorkflow();
    var plan = await workflow.CreatePlanAsync(request);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var result = await workflow.ExecuteAsync(plan);
    stopwatch.Stop();

    Assert.True(result.IsSuccessful);
    Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(350), "速度限制没有产生预期节流。" );
}

static async Task FileChangeTriggerDebouncesAndRunsTask()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "auto-trigger.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(
        Guid.NewGuid(), "实时触发", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    var trigger = new AutoTriggerService(
        repository,
        new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository)));
    var completion = new TaskCompletionSource<ScheduledRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    trigger.RunCompleted += (_, result) => completion.TrySetResult(result);
    await trigger.ConfigureAsync([
        new AutoTriggerRule(task.Id, AutoTriggerKind.FileChange, true, TimeSpan.FromSeconds(1)),
    ]);

    workspace.WriteSource("watched.txt", "first");
    workspace.WriteSource("watched.txt", "second-with-new-size");
    var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await trigger.DisposeAsync();

    Assert.True(result.Executed);
    Assert.Equal("second-with-new-size", workspace.ReadTarget("watched.txt"));
    Assert.Equal(1, (await repository.GetRecentRunsAsync()).Count);
}

static async Task SettingsStoreRoundTripAndCorruptionRecovery()
{
    using var workspace = TestWorkspace.Create();
    var path = Path.Combine(workspace.RootDirectory, "settings", "settings.json");
    var store = new JsonSettingsStore(path);
    var settings = new ApplicationSettings(false, false, true, "System", 30);

    await store.SaveAsync(settings);
    var loaded = await store.LoadAsync();
    Assert.Equal(settings, loaded);

    File.WriteAllText(path, "{not-json", Encoding.UTF8);
    var recovered = await store.LoadAsync();
    Assert.Equal(new ApplicationSettings(), recovered);
    Assert.Equal(1, Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.corrupt-*").Count());
}

static async Task ParallelCopyCompletesAllItems()
{
    using var workspace = TestWorkspace.Create();
    for (var index = 0; index < 24; index++)
    {
        workspace.WriteSource($"parallel-{index:00}.txt", new string((char)('a' + index % 26), 40_000));
    }

    var request = new CopyTaskRequest(
        workspace.SourceDirectory,
        workspace.TargetDirectory,
        CopyMode.AccumulateSync,
        VerificationMode: VerificationMode.Sha256,
        Performance: new PerformanceOptions(64 * 1024, MaxParallelFiles: 4));
    var workflow = new CopyWorkflow();
    var result = await workflow.ExecuteAsync(await workflow.CreatePlanAsync(request));

    Assert.True(result.IsSuccessful);
    Assert.Equal(24, result.SuccessfulFiles);
    Assert.Equal(24, result.EffectiveItems.Count);
    Assert.Equal(24, Directory.EnumerateFiles(workspace.TargetDirectory).Count());
}

static async Task AutoTriggerRulePersistsAndCascades()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "trigger-rule.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "trigger", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    await repository.SaveAutoTriggerRuleAsync(new AutoTriggerRule(
        task.Id, AutoTriggerKind.PathAvailable, true, TimeSpan.FromSeconds(8), true, false));

    var loaded = (await repository.GetAutoTriggerRulesAsync()).Single();
    Assert.Equal(AutoTriggerKind.PathAvailable, loaded.Kind);
    Assert.Equal(TimeSpan.FromSeconds(8), loaded.DebounceDelay);
    Assert.True(loaded.AllowOverwrite);

    await repository.DeleteTaskAsync(task.Id);
    Assert.Equal(0, (await repository.GetAutoTriggerRulesAsync()).Count);
}

static async Task LocalStorageAdapterTransfersSafely()
{
    using var workspace = TestWorkspace.Create();
    await using var adapter = new LocalStorageAdapter(workspace.SourceDirectory);
    await using var content = new MemoryStream(Encoding.UTF8.GetBytes("storage-payload"));
    await adapter.WriteAsync("nested/file.txt", content, content.Length);
    var items = await adapter.ListAsync();
    await using var read = await adapter.OpenReadAsync("nested/file.txt");
    using var reader = new StreamReader(read, Encoding.UTF8);

    Assert.Equal("storage-payload", await reader.ReadToEndAsync());
    Assert.True(items.Any(item => item.RelativePath == "nested/file.txt" && !item.IsDirectory));
    await Assert.ThrowsAsync<ArgumentException>(() => adapter.OpenReadAsync("../escape.txt"));
}

static async Task StorageTransferCopiesTree()
{
    using var workspace = TestWorkspace.Create();
    Directory.CreateDirectory(Path.Combine(workspace.SourceDirectory, "empty"));
    workspace.WriteSource("one.txt", "one");
    workspace.WriteSource(Path.Combine("nested", "two.txt"), "two");
    await using var source = new LocalStorageAdapter(workspace.SourceDirectory);
    await using var target = new LocalStorageAdapter(workspace.TargetDirectory);

    var result = await new StorageTransferService().CopyTreeAsync(source, target);

    Assert.True(result.IsSuccessful);
    Assert.Equal(2, result.SuccessfulFiles);
    Assert.Equal("two", workspace.ReadTarget(Path.Combine("nested", "two.txt")));
    Assert.True(Directory.Exists(Path.Combine(workspace.TargetDirectory, "empty")));
}

static Task WindowsCredentialStoreRoundTrip()
{
    if (!OperatingSystem.IsWindows())
    {
        return Task.CompletedTask;
    }

    var target = $"CopyTools.Tests/{Guid.NewGuid():N}";
    using var workspace = TestWorkspace.Create();
    var fallback = Path.Combine(workspace.RootDirectory, "encrypted-credentials");
    var store = new WindowsCredentialStore(fallback);
    try
    {
        store.Save(target, "测试用户", "不应明文落盘的秘密");
        var loaded = store.Read(target);
        Assert.NotNull(loaded);
        Assert.Equal("测试用户", loaded!.Value.UserName);
        Assert.Equal("不应明文落盘的秘密", loaded.Value.Secret);
        foreach (var path in Directory.Exists(fallback) ? Directory.EnumerateFiles(fallback) : [])
        {
            Assert.False(File.ReadAllText(path, Encoding.Latin1).Contains("不应明文落盘的秘密", StringComparison.Ordinal));
        }
    }
    finally
    {
        store.Delete(target);
    }

    Assert.True(store.Read(target) is null);
    return Task.CompletedTask;
}

static async Task WebDavAdapterSupportsCoreOperations()
{
    var requests = new List<(string Method, string Path, string? Body)>();
    var handler = new TestHttpHandler(async request =>
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));
        if (request.Method.Method == "PROPFIND")
        {
            return TestHttpHandler.Xml(
                """
                <?xml version="1.0"?>
                <d:multistatus xmlns:d="DAV:">
                  <d:response><d:href>/dav/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response>
                  <d:response><d:href>/dav/folder/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response>
                  <d:response><d:href>/dav/folder/file.txt</d:href><d:propstat><d:prop><d:resourcetype/><d:getcontentlength>7</d:getcontentlength><d:getlastmodified>Thu, 13 Aug 2026 00:00:00 GMT</d:getlastmodified><d:getetag>"etag"</d:getetag></d:prop></d:propstat></d:response>
                </d:multistatus>
                """);
        }

        if (request.Method == HttpMethod.Get)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("payload"),
            };
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.Created);
    });
    using var client = new HttpClient(handler);
    await using var adapter = new WebDavStorageAdapter(new Uri("https://example.test/dav/"), client);

    var items = await adapter.ListAsync();
    await using var read = await adapter.OpenReadAsync("folder/file.txt");
    using var reader = new StreamReader(read);
    Assert.Equal("payload", await reader.ReadToEndAsync());
    await using var upload = new MemoryStream(Encoding.UTF8.GetBytes("upload"));
    await adapter.WriteAsync("new.txt", upload, upload.Length);
    await adapter.CreateDirectoryAsync("new-folder");
    await adapter.MoveAsync("new.txt", "moved.txt", true);
    await adapter.DeleteAsync("moved.txt");

    Assert.Equal(2, items.Count);
    Assert.True(items.Any(item => item.IsDirectory && item.RelativePath == "folder"));
    Assert.True(items.Any(item => !item.IsDirectory && item.RelativePath == "folder/file.txt"));
    Assert.True(requests.Select(request => request.Method).SequenceEqual(["PROPFIND", "GET", "PUT", "MKCOL", "MOVE", "DELETE"]));
}

static async Task OneDriveAdapterUsesGraphAndChunkUpload()
{
    var requests = new List<(string Method, string Path, string? Range)>();
    var handler = new TestHttpHandler(request =>
    {
        requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath,
            request.Content?.Headers.ContentRange?.ToString()));
        if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/children", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.Json(
                """
                {"value":[{"name":"file.txt","size":7,"lastModifiedDateTime":"2026-08-13T00:00:00Z","eTag":"etag"},{"name":"folder","size":0,"folder":{}}]}
                """));
        }

        if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/createUploadSession", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.Json("{\"uploadUrl\":\"https://upload.test/session\"}"));
        }

        if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/content", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("payload") });
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Created)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
    });
    using var client = new HttpClient(handler);
    await using var adapter = new OneDriveStorageAdapter(
        "runtime-token", client: client, graphRoot: new Uri("https://graph.test/v1.0/"));

    var items = await adapter.ListAsync(recursive: false);
    await using var read = await adapter.OpenReadAsync("file.txt");
    using var reader = new StreamReader(read);
    Assert.Equal("payload", await reader.ReadToEndAsync());
    await using var small = new MemoryStream(Encoding.UTF8.GetBytes("small"));
    await adapter.WriteAsync("small.txt", small, small.Length);
    await using var large = new GeneratedStream(251L * 1024 * 1024);
    await adapter.WriteAsync("large.bin", large, large.Length);

    Assert.Equal(2, items.Count);
    Assert.True(requests.Any(request => request.Method == "PUT" && request.Path.Contains("small.txt", StringComparison.Ordinal)));
    Assert.True(requests.Count(request => request.Range is not null) > 1);
    Assert.Equal("bytes 0-10485759/263192576", requests.First(request => request.Range is not null).Range);
}

static async Task GoogleDriveAdapterResolvesIdsAndResumableUpload()
{
    var requests = new List<(string Method, string Path, string Query, string? Range)>();
    var handler = new TestHttpHandler(request =>
    {
        requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query,
            request.Content?.Headers.ContentRange?.ToString()));
        if (request.RequestUri.Host == "upload.test")
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }

        if (request.RequestUri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal)
            && request.Method == HttpMethod.Get)
        {
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (query.Contains("name='folder'", StringComparison.Ordinal))
            {
                return Task.FromResult(TestHttpHandler.Json(
                    "{\"files\":[{\"id\":\"folder-id\",\"name\":\"folder\",\"mimeType\":\"application/vnd.google-apps.folder\",\"parents\":[\"root\"]}]}"));
            }

            if (query.Contains("name='new.bin'", StringComparison.Ordinal))
            {
                return Task.FromResult(TestHttpHandler.Json("{\"files\":[]}"));
            }

            return Task.FromResult(TestHttpHandler.Json(
                "{\"files\":[{\"id\":\"file-id\",\"name\":\"file.txt\",\"mimeType\":\"text/plain\",\"size\":\"7\",\"parents\":[\"folder-id\"]}]}"));
        }

        if (request.RequestUri.AbsolutePath.Contains("/upload/drive/v3/files", StringComparison.Ordinal))
        {
            var response = TestHttpHandler.Json("{}");
            response.Headers.Location = new Uri("https://upload.test/session");
            return Task.FromResult(response);
        }

        if (request.Method == HttpMethod.Get && request.RequestUri.Query.Contains("alt=media", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("payload") });
        }

        return Task.FromResult(TestHttpHandler.Json("{\"id\":\"created\"}"));
    });
    using var client = new HttpClient(handler);
    await using var adapter = new GoogleDriveStorageAdapter(
        "token", client: client,
        apiRoot: new Uri("https://google.test/drive/v3/"),
        uploadRoot: new Uri("https://google.test/upload/drive/v3/"));

    var items = await adapter.ListAsync("folder", recursive: false);
    await using var upload = new GeneratedStream(10L * 1024 * 1024);
    await adapter.WriteAsync("folder/new.bin", upload, upload.Length);

    Assert.Equal(1, items.Count);
    Assert.True(requests.Any(request => request.Path.Contains("/upload/drive/v3/files", StringComparison.Ordinal)));
    Assert.Equal(2, requests.Count(request => request.Range is not null));
    Assert.Equal("bytes 0-8388607/10485760", requests.First(request => request.Range is not null).Range);
}

static async Task DropboxAdapterUsesPathApiAndUploadSession()
{
    var requests = new List<(string Path, string? Argument)>();
    var handler = new TestHttpHandler(request =>
    {
        request.Headers.TryGetValues("Dropbox-API-Arg", out var values);
        requests.Add((request.RequestUri!.AbsolutePath, values?.SingleOrDefault()));
        if (request.RequestUri.AbsolutePath.EndsWith("/files/list_folder", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.Json(
                "{\"entries\":[{\".tag\":\"file\",\"path_display\":\"/root/a.txt\",\"size\":1}],\"cursor\":\"c\",\"has_more\":false}"));
        }

        if (request.RequestUri.AbsolutePath.EndsWith("/upload_session/start", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.Json("{\"session_id\":\"session\"}"));
        }

        return Task.FromResult(TestHttpHandler.Json("{}"));
    });
    using var client = new HttpClient(handler);
    await using var adapter = new DropboxStorageAdapter(
        "token", "root", client,
        new Uri("https://dropbox.test/2/"), new Uri("https://content.dropbox.test/2/"));

    var items = await adapter.ListAsync(recursive: false);
    await using var upload = new GeneratedStream(151L * 1024 * 1024);
    await adapter.WriteAsync("large.bin", upload, upload.Length);

    Assert.Equal("a.txt", items.Single().RelativePath);
    Assert.True(requests.Any(request => request.Path.EndsWith("/upload_session/start", StringComparison.Ordinal)));
    Assert.True(requests.Any(request => request.Path.EndsWith("/upload_session/finish", StringComparison.Ordinal)));
    Assert.True(requests.Where(request => request.Argument is not null)
        .All(request => !request.Argument!.Contains("token", StringComparison.OrdinalIgnoreCase)));
}

static async Task S3AdapterSignsAndUsesMultipartUpload()
{
    var requests = new List<(string Method, string Path, string Query, string? Authorization, string? Range)>();
    var partNumber = 0;
    var handler = new TestHttpHandler(request =>
    {
        requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query,
            request.Headers.Authorization?.ToString() ?? request.Headers.GetValues("Authorization").SingleOrDefault(),
            request.Content?.Headers.ContentRange?.ToString()));
        if (request.Method == HttpMethod.Post && request.RequestUri.Query.StartsWith("?uploads", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.XmlOk("<InitiateMultipartUploadResult><UploadId>upload-id</UploadId></InitiateMultipartUploadResult>"));
        }

        if (request.Method == HttpMethod.Put && request.RequestUri.Query.Contains("partNumber", StringComparison.Ordinal))
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"part-{++partNumber}\"");
            return Task.FromResult(response);
        }

        if (request.Method == HttpMethod.Get && request.RequestUri.Query.Contains("list-type=2", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.XmlOk(
                "<ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>root/a.txt</Key><Size>1</Size><LastModified>2026-08-13T00:00:00Z</LastModified><ETag>etag</ETag></Contents></ListBucketResult>"));
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("<ok/>") });
    });
    using var client = new HttpClient(handler);
    await using var adapter = new S3StorageAdapter(
        "https://s3.test/", "us-east-1", "bucket", "ACCESS", "SECRET", "root", client: client);

    var items = await adapter.ListAsync(recursive: true);
    await using var upload = new GeneratedStream(33L * 1024 * 1024);
    await adapter.WriteAsync("large.bin", upload, upload.Length);

    Assert.Equal("a.txt", items.Single().RelativePath);
    Assert.Equal(3, requests.Count(request => request.Query.Contains("partNumber", StringComparison.Ordinal)));
    Assert.True(requests.All(request => request.Authorization?.StartsWith("AWS4-HMAC-SHA256", StringComparison.Ordinal) == true));
    Assert.True(requests.All(request => request.Authorization?.Contains("SECRET", StringComparison.Ordinal) == false));
    Assert.True(requests.Any(request => request.Method == "POST" && request.Query.Contains("uploadId=upload-id", StringComparison.Ordinal)));
}

static async Task BaiduNetdiskAdapterUsesThreePhaseUpload()
{
    var requests = new List<(string Method, string MethodValue, string Query, string? Body)>();
    var uploadParts = 0;
    var handler = new TestHttpHandler(async request =>
    {
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        var methodValue = query.Split('&', '?')
            .Select(value => value.Split('=', 2))
            .FirstOrDefault(value => value.Length == 2 && value[0] == "method")?[1] ?? string.Empty;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        requests.Add((request.Method.Method, methodValue, request.RequestUri.Query, body));

        if (methodValue == "list")
        {
            return TestHttpHandler.Json(
                "{\"errno\":0,\"list\":[{\"fs_id\":1,\"server_filename\":\"file.txt\",\"path\":\"/root/file.txt\",\"isdir\":0,\"size\":7,\"server_mtime\":1786579200,\"md5\":\"etag\"}]}");
        }

        if (methodValue == "filemetas")
        {
            return TestHttpHandler.Json("{\"errno\":0,\"list\":[{\"dlink\":\"https://download.test/file\"}]}");
        }

        if (request.RequestUri.Host == "download.test")
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("payload") };
        }

        if (methodValue == "precreate")
        {
            return TestHttpHandler.Json("{\"errno\":0,\"uploadid\":\"upload-id\",\"block_list\":[0,1]}");
        }

        if (methodValue == "upload")
        {
            uploadParts++;
            return TestHttpHandler.Json("{\"md5\":\"part\"}");
        }

        return TestHttpHandler.Json("{\"errno\":0}");
    });
    using var client = new HttpClient(handler);
    await using var adapter = new BaiduNetdiskStorageAdapter(
        "runtime-token", "root", client,
        new Uri("https://pan.test/rest/2.0/xpan/file"),
        new Uri("https://upload.test/rest/2.0/pcs/superfile2"));

    var items = await adapter.ListAsync(recursive: false);
    await using var read = await adapter.OpenReadAsync("file.txt");
    using var reader = new StreamReader(read);
    Assert.Equal("payload", await reader.ReadToEndAsync());
    await using var upload = new GeneratedStream(6L * 1024 * 1024);
    await adapter.WriteAsync("large.bin", upload, upload.Length);
    await adapter.CreateDirectoryAsync("folder");
    await adapter.MoveAsync("large.bin", "folder/moved.bin", true);
    await adapter.DeleteAsync("folder/moved.bin");

    Assert.Equal("file.txt", items.Single().RelativePath);
    Assert.Equal(2, uploadParts);
    Assert.True(requests.Any(request => request.MethodValue == "precreate" && request.Body!.Contains("block_list", StringComparison.Ordinal)));
    Assert.True(requests.Any(request => request.MethodValue == "create" && request.Body!.Contains("uploadid=upload-id", StringComparison.Ordinal)));
    Assert.True(requests.Any(request => request.MethodValue == "filemanager" && request.Query.Contains("opera=move", StringComparison.Ordinal)));
    Assert.True(requests.All(request => request.Body?.Contains("runtime-token", StringComparison.Ordinal) != true));
}

[SupportedOSPlatform("windows")]
static async Task StorageConnectionPersistsReferenceAndTestsLocal()
{
    using var workspace = TestWorkspace.Create();
    var databasePath = Path.Combine(workspace.RootDirectory, "connections.db");
    var credentialDirectory = Path.Combine(workspace.RootDirectory, "credentials");
    var repository = new SqliteCopyToolsRepository(databasePath);
    await repository.InitializeAsync();
    workspace.WriteSource("visible.txt", "data");
    var profile = StorageConnectionProfile.Create(
        "local-test", StorageConnectionKind.Local, workspace.SourceDirectory);
    var service = new StorageConnectionService(repository, new WindowsCredentialStore(credentialDirectory));

    await service.SaveAsync(profile, null);
    var loaded = (await service.GetAllAsync()).Single();
    var itemCount = await service.TestAsync(loaded);

    Assert.Equal(profile.CredentialTarget, loaded.CredentialTarget);
    Assert.Equal(1, itemCount);
    Assert.False(File.ReadAllText(databasePath, Encoding.UTF8).Contains("runtime-secret", StringComparison.Ordinal));
    await service.DeleteAsync(loaded);
    Assert.Equal(0, (await service.GetAllAsync()).Count);
}

static async Task TaskOrchestrationMetadataAndControlledCommand()
{
    using var workspace = TestWorkspace.Create();
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "orchestration.db"));
    await repository.InitializeAsync();
    var task = new SavedCopyTask(
        Guid.NewGuid(), "metadata", new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.IncrementalCopy),
        DateTime.UtcNow, DateTime.UtcNow);
    await repository.SaveTaskAsync(task);
    var dependency = Guid.NewGuid();
    var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    var options = new TaskExecutionOptions(
        task.Id, "description", "group", ["daily", "safe"], 25, [dependency],
        new ControlledCommand(executable, ["/d", "/c", "echo", "structured-output"], Timeout: TimeSpan.FromSeconds(10)));
    await repository.SaveTaskExecutionOptionsAsync(options);

    var loaded = await repository.GetTaskExecutionOptionsAsync(task.Id);
    var commandResult = await new ControlledCommandRunner().RunAsync(options.BeforeCommand!);

    Assert.NotNull(loaded);
    Assert.Equal("group", loaded!.Group);
    Assert.Equal(dependency, loaded.EffectiveDependencies.Single());
    Assert.True(commandResult.Successful);
    Assert.True(commandResult.StandardOutput.Contains("structured-output", StringComparison.Ordinal));
}

static async Task TaskQueueHonorsDependencies()
{
    using var workspace = TestWorkspace.Create();
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "queue.db"));
    await repository.InitializeAsync();
    var firstTarget = Path.Combine(workspace.RootDirectory, "target-first");
    var secondTarget = Path.Combine(workspace.RootDirectory, "target-second");
    Directory.CreateDirectory(firstTarget);
    Directory.CreateDirectory(secondTarget);
    workspace.WriteSource("queue.txt", "queue-data");
    var now = DateTime.UtcNow;
    var first = new SavedCopyTask(
        Guid.NewGuid(), "first", new CopyTaskRequest(workspace.SourceDirectory, firstTarget, CopyMode.IncrementalCopy), now, now);
    var second = new SavedCopyTask(
        Guid.NewGuid(), "second", new CopyTaskRequest(workspace.SourceDirectory, secondTarget, CopyMode.IncrementalCopy), now, now);
    await repository.SaveTaskAsync(first);
    await repository.SaveTaskAsync(second);
    await repository.SaveTaskExecutionOptionsAsync(new TaskExecutionOptions(first.Id, Priority: -10));
    await repository.SaveTaskExecutionOptionsAsync(new TaskExecutionOptions(second.Id, Priority: 100, Dependencies: [first.Id]));
    var workflow = new CopyWorkflow(new CopyPlanService(snapshotStore: repository));
    var queue = new TaskQueueService(repository, new ScheduledTaskRunner(repository, workflow));

    var result = await queue.RunAsync([second.Id, first.Id], maximumParallelTasks: 2);

    Assert.True(result.IsSuccessful);
    Assert.Equal(first.Id, result.Items[0].TaskId);
    Assert.Equal(second.Id, result.Items[1].TaskId);
    Assert.True(File.Exists(Path.Combine(firstTarget, "queue.txt")));
    Assert.True(File.Exists(Path.Combine(secondTarget, "queue.txt")));
}

static async Task LargeFileResumesFromPartial()
{
    using var workspace = TestWorkspace.Create();
    var sourcePath = Path.Combine(workspace.SourceDirectory, "large.bin");
    await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x5A, 8 * 1024 * 1024).ToArray());
    var targetPath = Path.Combine(workspace.TargetDirectory, "large.bin");
    using var cancellation = new CancellationTokenSource();
    long firstRunBytes = 0;
    var firstProgress = new InlineTestProgress<long>(bytes =>
    {
        firstRunBytes += bytes;
        if (firstRunBytes >= 2 * 1024 * 1024) cancellation.Cancel();
    });
    var copier = new SafeFileCopier();
    await Assert.ThrowsAsync<OperationCanceledException>(() => copier.CopyAsync(
        sourcePath, targetPath, File.GetLastWriteTimeUtc(sourcePath), FileAttributes.Normal,
        false, VerificationMode.Sha256, firstProgress, cancellation.Token,
        performanceOptions: new PerformanceOptions(1024 * 1024, null, 1)));
    var partialPath = Path.Combine(workspace.TargetDirectory, ".large.bin.copytools-partial");
    Assert.True(File.Exists(partialPath));
    var partialLength = new FileInfo(partialPath).Length;
    long resumedProgress = 0;
    await copier.CopyAsync(
        sourcePath, targetPath, File.GetLastWriteTimeUtc(sourcePath), FileAttributes.Normal,
        false, VerificationMode.Sha256, new InlineTestProgress<long>(bytes => resumedProgress += bytes),
        CancellationToken.None, performanceOptions: new PerformanceOptions(1024 * 1024, null, 1));

    Assert.Equal(8L * 1024 * 1024, resumedProgress);
    Assert.True(partialLength > 0);
    Assert.True(File.Exists(targetPath));
    Assert.False(File.Exists(partialPath));
}

static async Task ExecutionCheckpointResumesSafely()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.txt", "a");
    workspace.WriteSource("b.txt", "b");
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "checkpoint.db"));
    await repository.InitializeAsync();
    var task = new SavedCopyTask(
        Guid.NewGuid(), "checkpoint",
        new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.IncrementalCopy),
        DateTime.UtcNow, DateTime.UtcNow);
    await repository.SaveTaskAsync(task);
    var workflow = new CopyWorkflow(new CopyPlanService(snapshotStore: repository));
    var plan = await workflow.CreatePlanAsync(task.Request);
    var firstItem = plan.Items.First(item => item.RelativePath == "a.txt");
    File.Copy(firstItem.SourcePath, firstItem.TargetPath);
    File.SetLastWriteTimeUtc(firstItem.TargetPath, firstItem.LastWriteTimeUtc);
    var execution = new IncompleteExecution(
        Guid.NewGuid(), task.Id, task.Request, plan.CreatedAtUtc, DateTime.UtcNow,
        [new IncompleteExecutionItem(firstItem.RelativePath, firstItem.Action, firstItem.Length, firstItem.LastWriteTimeUtc, DateTime.UtcNow)]);
    await repository.SaveIncompleteExecutionAsync(execution);
    await repository.SaveIncompleteExecutionItemAsync(execution.Id, execution.CompletedItems.Single());

    var resumable = new ResumableExecutionService(repository, workflow);
    var (resumedPlan, result) = await resumable.ResumeAsync(task.Id);

    Assert.True(result.IsSuccessful);
    Assert.False(resumedPlan.Items.Any(item => item.RelativePath == "a.txt" && item.WillExecute));
    Assert.True(File.Exists(Path.Combine(workspace.TargetDirectory, "b.txt")));
    Assert.Equal(0, (await repository.GetIncompleteExecutionsAsync()).Count);
}

static async Task SecureArchiveBackupAuthenticatesAndRestores()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("文档.txt", "sensitive-content");
    workspace.WriteSource(Path.Combine("nested", "data.bin"), "binary-data");
    var archivePath = Path.Combine(workspace.RootDirectory, "backup.ctbackup");
    var restorePath = Path.Combine(workspace.RootDirectory, "archive-restore");
    var service = new SecureBackupArchiveService();

    var result = await service.CreateAsync(
        workspace.SourceDirectory, archivePath, compress: true, password: "correct horse battery staple");
    await service.RestoreAsync(archivePath, restorePath, "correct horse battery staple");

    Assert.True(result.Encrypted);
    Assert.Equal("sensitive-content", File.ReadAllText(Path.Combine(restorePath, "文档.txt"), Encoding.UTF8));
    await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
        service.RestoreAsync(archivePath, Path.Combine(workspace.RootDirectory, "wrong-password"), "wrong"));
    var bytes = await File.ReadAllBytesAsync(archivePath);
    bytes[^1] ^= 0x01;
    var tampered = Path.Combine(workspace.RootDirectory, "tampered.ctbackup");
    await File.WriteAllBytesAsync(tampered, bytes);
    await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
        service.RestoreAsync(tampered, Path.Combine(workspace.RootDirectory, "tampered-restore"), "correct horse battery staple"));
}

[SupportedOSPlatform("windows")]
static async Task WebhookNotificationSendsSummaryWithoutSecretBody()
{
    using var workspace = TestWorkspace.Create();
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "notify.db"));
    await repository.InitializeAsync();
    var credentials = new WindowsCredentialStore(Path.Combine(workspace.RootDirectory, "credentials"));
    var target = $"CopyTools/Notification/{Guid.NewGuid():D}";
    credentials.Save(target, "", "webhook-secret");
    var channel = new NotificationChannel(
        Guid.NewGuid(), "test-webhook", NotificationChannelKind.Webhook, true,
        "https://hooks.test/copytools", "", target, "", "");
    await repository.SaveNotificationChannelAsync(channel);
    string? body = null;
    string? authorization = null;
    var handler = new TestHttpHandler(async request =>
    {
        body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        authorization = request.Headers.Authorization?.ToString();
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    });
    using var client = new HttpClient(handler);
    var service = new TaskNotificationService(repository, credentials, client);

    var errors = await service.SendAsync(new TaskResultNotification(
        Guid.NewGuid(), "notify-task", "成功", DateTime.UtcNow, 3, 0, 1234));

    Assert.Equal(0, errors.Count);
    Assert.True(body!.Contains("notify-task", StringComparison.Ordinal));
    Assert.False(body.Contains("webhook-secret", StringComparison.Ordinal));
    Assert.Equal("Bearer webhook-secret", authorization);
    credentials.Delete(target);
}

static Task PathTemplateExpandsSafely()
{
    var expanded = PathTemplateExpander.ExpandPath(
        @"C:\backup\{task}\{date:yyyy-MM-dd}\${TEMP}",
        "Daily:Docs", new DateTime(2026, 8, 13));
    Assert.True(expanded.Contains(@"Daily_Docs\2026-08-13", StringComparison.Ordinal));
    Assert.True(expanded.Contains(Environment.GetEnvironmentVariable("TEMP")!, StringComparison.OrdinalIgnoreCase));
    try
    {
        _ = PathTemplateExpander.ExpandPath(@"C:\${PATH}", "task", DateTime.Now);
        throw new InvalidOperationException("预期拒绝未授权环境变量。");
    }
    catch (CopyTaskValidationException)
    {
    }
    return Task.CompletedTask;
}

static async Task OverwriteVersionCanBePreservedAndRestored()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("versioned.txt", "new-value", DateTime.UtcNow);
    workspace.WriteTarget("versioned.txt", "old-value", DateTime.UtcNow.AddHours(-1));
    var versions = Path.Combine(workspace.RootDirectory, "versions");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.FullCopy,
        Versioning: new VersioningPolicy(true, versions, 5));
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);
    var entry = new FileVersionService().GetVersions(versions).Single();
    var savedVersion = entry.VersionPath;
    var restored = Path.Combine(workspace.RootDirectory, "restored-old.txt");
    new FileVersionService().Restore(savedVersion, restored, overwrite: false);

    Assert.True(result.IsSuccessful);
    Assert.Equal("versioned.txt", entry.RelativePath);
    Assert.Equal(Path.Combine(workspace.TargetDirectory, "versioned.txt"), entry.OriginalTargetPath);
    Assert.Equal("new-value", workspace.ReadTarget("versioned.txt"));
    Assert.Equal("old-value", File.ReadAllText(restored, Encoding.UTF8));
}

static async Task VersionDirectoryMustBeIsolated()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("nested.txt", "value");
    await Assert.ThrowsAsync<CopyTaskValidationException>(() => new CopyPlanService().CreatePlanAsync(
        new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.FullCopy,
            Versioning: new VersioningPolicy(true, Path.Combine(workspace.SourceDirectory, "versions")))));
    await Assert.ThrowsAsync<CopyTaskValidationException>(() => new CopyPlanService().CreatePlanAsync(
        new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.FullCopy,
            Versioning: new VersioningPolicy(true, null))));
}

[SupportedOSPlatform("windows")]
static async Task CopyCanPreserveWindowsAcl()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("acl.txt", "acl-value");
    var source = Path.Combine(workspace.SourceDirectory, "acl.txt");
    var target = Path.Combine(workspace.TargetDirectory, "acl.txt");
    var sourceSecurity = new FileInfo(source).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
    var request = new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.IncrementalCopy,
        Security: new FileSecurityOptions(PreserveNtfsPermissions: true));
    var result = await new CopyExecutor().ExecuteAsync(await new CopyPlanService().CreatePlanAsync(request));
    var targetSecurity = new FileInfo(target).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);

    Assert.True(result.IsSuccessful);
    Assert.Equal(
        sourceSecurity.GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access),
        targetSecurity.GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access));
}

static async Task PluginCatalogLoadsAndVerifierRuns()
{
    using var workspace = TestWorkspace.Create();
    var pluginDirectory = Path.Combine(workspace.RootDirectory, "plugins", "sample");
    Directory.CreateDirectory(pluginDirectory);
    var assemblyPath = typeof(SampleCopyToolsPlugin).Assembly.Location;
    var targetAssembly = Path.Combine(pluginDirectory, Path.GetFileName(assemblyPath));
    File.Copy(assemblyPath, targetAssembly);
    await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "plugin.json"),
        $$"""
        {
          "id": "copytools.tests.sample",
          "name": "测试插件",
          "assembly": "{{Path.GetFileName(assemblyPath)}}",
          "type": "SampleCopyToolsPlugin",
          "apiMajorVersion": 1,
          "enabled": true
        }
        """);
    var broken = Path.Combine(workspace.RootDirectory, "plugins", "broken");
    Directory.CreateDirectory(broken);
    await File.WriteAllTextAsync(Path.Combine(broken, "plugin.json"), "{ invalid json");

    var catalog = PluginCatalog.Load(Path.Combine(workspace.RootDirectory, "plugins"));
    Assert.Equal(1, catalog.Plugins.Count);
    Assert.True(catalog.StorageFactories.ContainsKey("tests.memory"));
    Assert.True(catalog.VerificationProviders.ContainsKey("tests.equal"));
    Assert.Equal(1, catalog.Diagnostics.Count);

    workspace.WriteSource("plugin.txt", "plugin verified");
    var request = workspace.Request(CopyMode.FullCopy) with { VerificationProviderId = "tests.equal" };
    var workflow = new CopyWorkflow(
        new CopyPlanService(),
        new CopyExecutor(verificationProviders: catalog.VerificationProviders));
    var plan = await workflow.CreatePlanAsync(request);
    var result = await workflow.ExecuteAsync(plan);
    Assert.True(result.IsSuccessful);

    await using var adapter = catalog.StorageFactories["tests.memory"].Create(
        StorageConnectionProfile.Create("plugin", StorageConnectionKind.Plugin, "tests.memory"), "", "");
    Assert.Equal("测试内存存储", adapter.DisplayName);
}

static async Task BackupManifestCanKeepOriginalSource()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("data.txt", "manifest");
    var repository = Path.Combine(workspace.RootDirectory, "repository");
    var original = Path.Combine(workspace.RootDirectory, "original-source-name");
    var service = new BackupRepositoryService();
    var result = await service.CreateBackupAsync(new BackupTaskRequest(
        workspace.SourceDirectory, repository, BackupMode.IncrementalHistory,
        ManifestSourceDirectory: original));
    Assert.Equal(Path.GetFullPath(original), result.Snapshot.SourceDirectory);
    var loaded = (await service.GetSnapshotsAsync(repository)).Single();
    Assert.Equal(Path.GetFullPath(original), loaded.SourceDirectory);
}

static async Task MultiSourceCopyUsesIsolatedTargets()
{
    using var workspace = TestWorkspace.Create();
    var first = Path.Combine(workspace.RootDirectory, "source-a");
    var second = Path.Combine(workspace.RootDirectory, "source-b");
    Directory.CreateDirectory(first); Directory.CreateDirectory(second);
    await File.WriteAllTextAsync(Path.Combine(first, "same.txt"), "A");
    await File.WriteAllTextAsync(Path.Combine(second, "same.txt"), "B");
    var singleA = Path.Combine(workspace.RootDirectory, "loose-a", "loose.txt");
    var singleB = Path.Combine(workspace.RootDirectory, "loose-b", "loose.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(singleA)!); Directory.CreateDirectory(Path.GetDirectoryName(singleB)!);
    await File.WriteAllTextAsync(singleA, "one"); await File.WriteAllTextAsync(singleB, "two");
    var request = new CopyTaskRequest(first, workspace.TargetDirectory, CopyMode.AccumulateSync,
        SourcePaths: [first, second, singleA, singleB]);
    var workflow = new CopyWorkflow();
    var plan = await workflow.CreatePlanAsync(request);
    Assert.Equal(4, plan.FilesToCopy);
    var result = await workflow.ExecuteAsync(plan);
    Assert.True(result.IsSuccessful);
    Assert.Equal("A", await File.ReadAllTextAsync(Path.Combine(workspace.TargetDirectory, "source-a", "same.txt")));
    Assert.Equal("B", await File.ReadAllTextAsync(Path.Combine(workspace.TargetDirectory, "source-b", "same.txt")));
    Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(workspace.TargetDirectory, "loose.txt")));
    Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(workspace.TargetDirectory, "loose (2).txt")));
}

static async Task MultiSourceRejectsDestructiveModes()
{
    using var workspace = TestWorkspace.Create();
    var other = Path.Combine(workspace.RootDirectory, "other");
    Directory.CreateDirectory(other);
    await Assert.ThrowsAsync<CopyTaskValidationException>(() => new CopyPlanService().CreatePlanAsync(
        new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.MirrorSync,
            SourcePaths: [workspace.SourceDirectory, other])));
}

static async Task TemplatesPersistRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "templates.db");
    var now = DateTime.UtcNow;
    var filter = new FilterRuleTemplate(Guid.NewGuid(), "文档", new FileFilterOptions(["*.docx"], ["~*"]), now, now);
    var task = new TaskTemplate(Guid.NewGuid(), "快速备份", CopyMode.AccumulateSync, filter.Filters,
        VerificationMode.Sha256, new RetryPolicy(4, TimeSpan.FromSeconds(2)),
        new DeleteSafetyPolicy(20, 5), ExistingFilePolicy.ModeDefault,
        new PerformanceOptions(256 * 1024, MaxParallelFiles: 2), now, now);
    await repository.SaveFilterRuleTemplateAsync(filter); await repository.SaveTaskTemplateAsync(task);
    var loadedFilter = (await repository.GetFilterRuleTemplatesAsync()).Single();
    Assert.Equal(filter.Id, loadedFilter.Id); Assert.Equal(filter.Name, loadedFilter.Name);
    Assert.Equal("*.docx", loadedFilter.Filters.EffectiveIncludePatterns.Single());
    Assert.Equal("~*", loadedFilter.Filters.EffectiveExcludePatterns.Single());
    var loadedTask = (await repository.GetTaskTemplatesAsync()).Single();
    Assert.Equal(task.Id, loadedTask.Id); Assert.Equal(task.Name, loadedTask.Name);
    Assert.Equal(task.Mode, loadedTask.Mode); Assert.Equal(task.VerificationMode, loadedTask.VerificationMode);
    Assert.Equal(task.RetryPolicy, loadedTask.RetryPolicy); Assert.Equal(task.Performance, loadedTask.Performance);
    await repository.DeleteFilterRuleTemplateAsync(filter.Id); await repository.DeleteTaskTemplateAsync(task.Id);
    Assert.Equal(0, (await repository.GetFilterRuleTemplatesAsync()).Count);
    Assert.Equal(0, (await repository.GetTaskTemplatesAsync()).Count);
}

static async Task DuplicateDetectorHashesCandidates()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.bin", "same-content"); workspace.WriteSource("nested/b.bin", "same-content");
    workspace.WriteSource("c.bin", "different-one"); workspace.WriteSource("d.bin", "different-two");
    var groups = await new DuplicateFileDetector().ScanAsync([workspace.SourceDirectory]);
    Assert.Equal(1, groups.Count); Assert.Equal(2, groups[0].Files.Count);
    Assert.True(groups[0].Files.All(file => file.Path.EndsWith("a.bin") || file.Path.EndsWith("b.bin")));
}

static async Task ConfigurationBackupRestoresWithoutSecrets()
{
    using var workspace = TestWorkspace.Create();
    var source = await CreateRepository(workspace, "config-source.db");
    var sourceSettings = new JsonSettingsStore(Path.Combine(workspace.RootDirectory, "source-settings.json"));
    var now = DateTime.UtcNow;
    var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
    var request = new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync);
    await source.SaveTaskAsync(new SavedCopyTask(firstId, "前置", request, now, now));
    await source.SaveTaskAsync(new SavedCopyTask(secondId, "主体", request, now, now));
    await source.SaveScheduleAsync(new TaskSchedule(secondId, true, ScheduleTrigger.Daily, TimeSpan.FromHours(3),
        DayOfWeek.Monday, 1, null, MissedRunPolicy.RunImmediately, true, false));
    await source.SaveTaskExecutionOptionsAsync(new TaskExecutionOptions(secondId, Dependencies: [firstId]));
    var connection = StorageConnectionProfile.Create("远端", StorageConnectionKind.WebDav, "https://example.invalid");
    await source.SaveStorageConnectionAsync(connection);
    var channelId = Guid.NewGuid();
    await source.SaveNotificationChannelAsync(new NotificationChannel(channelId, "通知", NotificationChannelKind.Webhook,
        true, "https://example.invalid/hook", "token", $"CopyTools/notification/{channelId:D}", "", ""));
    await sourceSettings.SaveAsync(new ApplicationSettings(Theme: "Dark", ProxyUrl: "http://proxy.invalid",
        ProxyCredentialTarget: "CopyTools/Proxy"));
    var exportPath = Path.Combine(workspace.RootDirectory, "configuration.json");
    await new ConfigurationBackupService(source, sourceSettings).ExportAsync(exportPath);
    var json = await File.ReadAllTextAsync(exportPath);
    Assert.False(json.Contains("actual-password", StringComparison.Ordinal));

    var target = await CreateRepository(workspace, "config-target.db");
    var targetSettings = new JsonSettingsStore(Path.Combine(workspace.RootDirectory, "target-settings.json"));
    var result = await new ConfigurationBackupService(target, targetSettings).ImportAsync(exportPath, true);
    Assert.Equal(2, result.Tasks);
    Assert.Equal(1, (await target.GetSchedulesAsync()).Count);
    var options = (await target.GetAllTaskExecutionOptionsAsync()).Single();
    Assert.Equal(1, options.EffectiveDependencies.Count);
    Assert.Equal(1, (await target.GetStorageConnectionsAsync()).Count);
    Assert.Equal(1, (await target.GetNotificationChannelsAsync()).Count);
    Assert.Equal("Dark", (await targetSettings.LoadAsync()).Theme);
}

static Task WindowsServiceArgumentsAreQuotedSafely()
{
    Assert.Equal("plain", WindowsServiceManager.QuoteArgument("plain"));
    Assert.Equal("\"C:\\Program Files\\CopyTools\\service.exe\"",
        WindowsServiceManager.QuoteArgument("C:\\Program Files\\CopyTools\\service.exe"));
    Assert.Equal("\"C:\\Data Folder\\\\\"", WindowsServiceManager.QuoteArgument("C:\\Data Folder\\"));
    return Task.CompletedTask;
}

static async Task TwoWayConflictMustBeResolvedBeforeExecution()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("conflict.txt", "source-version");
    await File.WriteAllTextAsync(Path.Combine(workspace.TargetDirectory, "conflict.txt"), "target-version");
    var repository = await CreateRepository(workspace, "two-way-ask.db");
    var request = new CopyTaskRequest(workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.TwoWaySync,
        SynchronizationId: Guid.NewGuid(), ConflictResolutionPolicy: ConflictResolutionPolicy.Ask);
    var workflow = CreateSyncWorkflow(repository);
    var plan = await workflow.CreatePlanAsync(request);
    Assert.Equal(1, plan.Conflicts); Assert.Equal(1, plan.UnresolvedConflicts);
    await Assert.ThrowsAsync<CopyTaskValidationException>(() => workflow.ExecuteAsync(plan));
    var resolved = PlanEditor.ResolveConflict(plan, "conflict.txt", ConflictResolutionPolicy.SourceWins);
    Assert.Equal(0, resolved.UnresolvedConflicts); Assert.Equal(1, resolved.Conflicts);
    var result = await workflow.ExecuteAsync(resolved);
    Assert.True(result.IsSuccessful);
    Assert.Equal("source-version", await File.ReadAllTextAsync(Path.Combine(workspace.TargetDirectory, "conflict.txt")));
}

static async Task ReparsePointPolicyControlsFileLinks()
{
    using var workspace = TestWorkspace.Create();
    var real = Path.Combine(workspace.SourceDirectory, "real.txt");
    await File.WriteAllTextAsync(real, "linked-content");
    var link = Path.Combine(workspace.SourceDirectory, "linked.txt");
    try { File.CreateSymbolicLink(link, real); }
    catch (UnauthorizedAccessException)
    {
        Assert.Equal(ReparsePointPolicy.Skip, new FileFilterOptions().ReparsePointPolicy);
        Assert.Equal(ReparsePointPolicy.FollowAll,
            new FileFilterOptions(ReparsePointPolicy: ReparsePointPolicy.FollowAll).ReparsePointPolicy);
        return;
    }
    var scanner = new DirectoryScanner();
    var skipped = await scanner.ScanAsync(workspace.SourceDirectory, new FileFilterOptions());
    var followed = await scanner.ScanAsync(workspace.SourceDirectory,
        new FileFilterOptions(ReparsePointPolicy: ReparsePointPolicy.FollowFileLinks));
    Assert.Equal(1, skipped.Count); Assert.Equal(2, followed.Count);
    Assert.True(followed.Any(file => file.RelativePath == "linked.txt"));
    File.Delete(link);
}

static async Task UpdateServiceVerifiesLocalPackage()
{
    using var workspace = TestWorkspace.Create();
    var package = Path.Combine(workspace.RootDirectory, "CopyTools-Portable-1.1.0.zip");
    var data = Encoding.UTF8.GetBytes("verified update package");
    await File.WriteAllBytesAsync(package, data);
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
    var manifest = Path.Combine(workspace.RootDirectory, "update.json");
    await File.WriteAllTextAsync(manifest,
        $$"""{"formatVersion":1,"version":"1.1.0","downloadUrl":"{{Path.GetFileName(package)}}","sha256":"{{hash}}","size":{{data.Length}},"releaseNotes":"test"}""");
    var service = new UpdateService();
    var check = await service.CheckAsync(manifest, new Version(1, 0, 0));
    Assert.True(check.IsUpdateAvailable);
    var downloaded = Path.Combine(workspace.RootDirectory, "download", "package.zip");
    await service.DownloadAsync(check.Manifest, downloaded);
    Assert.Equal("verified update package", await File.ReadAllTextAsync(downloaded));
    await File.WriteAllTextAsync(manifest,
        $$"""{"formatVersion":1,"version":"1.1.0","downloadUrl":"{{Path.GetFileName(package)}}","sha256":"{{new string('0', 64)}}","size":{{data.Length}}}""");
    var tampered = await service.CheckAsync(manifest, new Version(1, 0, 0));
    await Assert.ThrowsAsync<InvalidDataException>(() =>
        service.DownloadAsync(tampered.Manifest, Path.Combine(workspace.RootDirectory, "tampered.zip")));
}

static async Task DeltaFileCopierReconstructsChangedBlocks()
{
    using var workspace = TestWorkspace.Create();
    const int prefixLength = 4 * 1024 * 1024;
    var sourcePath = Path.Combine(workspace.SourceDirectory, "delta.bin");
    var targetPath = Path.Combine(workspace.TargetDirectory, "delta.bin");
    var sourceBytes = new byte[prefixLength + 1024];
    Array.Fill(sourceBytes, (byte)0x41);
    Array.Fill(sourceBytes, (byte)0x42, prefixLength, 1024);
    var targetBytes = new byte[prefixLength + 1024];
    Array.Fill(targetBytes, (byte)0x41);
    await File.WriteAllBytesAsync(sourcePath, sourceBytes);
    await File.WriteAllBytesAsync(targetPath, targetBytes);

    var temporaryPath = Path.Combine(workspace.TargetDirectory, ".delta.bin.copytools-partial");
    var result = await new DeltaFileCopier().ReconstructAsync(sourcePath, targetPath, temporaryPath);
    var output = await File.ReadAllBytesAsync(temporaryPath);

    Assert.True(result.ChangedBytes > 0, "应识别出发生变化的块。");
    Assert.True(result.ReusedBytes > 0, "应复用未变化的块。");
    Assert.True(result.BlockSizeBytes >= 64 * 1024);
    Assert.Equal(sourceBytes.Length, result.TotalBytes);
    Assert.True(sourceBytes.AsSpan().SequenceEqual(output), "重建后的内容应与源一致。");
}

static async Task DeltaCopyModeOverwritesViaExecutor()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("delta.txt", "new-source-content-longer-than-target");
    workspace.WriteTarget("delta.txt", "old");
    var request = new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync,
        TransferMode: TransferMode.Delta);
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.Equal(CopyAction.Overwrite, plan.Items.Single().Action);
    Assert.True(result.IsSuccessful);
    Assert.Equal("new-source-content-longer-than-target", workspace.ReadTarget("delta.txt"));
}

static async Task SampledSha256DetectsSameMetadataContentChange()
{
    using var workspace = TestWorkspace.Create();
    var timestamp = DateTime.UtcNow.AddMinutes(-5);
    workspace.WriteSource("sampled.bin", "AAAA", timestamp);
    workspace.WriteTarget("sampled.bin", "BBBB", timestamp);
    var quick = await new CopyPlanService().CreatePlanAsync(new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync));
    var sampled = await new CopyPlanService().CreatePlanAsync(new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync,
        VerificationMode: VerificationMode.SampledSha256));

    Assert.Equal(0, quick.FilesToOverwrite);
    Assert.Equal(1, sampled.FilesToOverwrite);
    Assert.Equal(CopyAction.Overwrite, sampled.Items.Single().Action);
}

static async Task SampledSha256VerificationCopiesFile()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("sampled-copy.bin", new string('s', 1024 * 1024));
    var request = new CopyTaskRequest(
        workspace.SourceDirectory, workspace.TargetDirectory, CopyMode.AccumulateSync,
        VerificationMode: VerificationMode.SampledSha256,
        RetryPolicy: new RetryPolicy(1, TimeSpan.Zero));
    var plan = await new CopyPlanService().CreatePlanAsync(request);
    var result = await new CopyExecutor().ExecuteAsync(plan);

    Assert.True(result.IsSuccessful);
    Assert.Equal(new string('s', 1024 * 1024), workspace.ReadTarget("sampled-copy.bin"));
}

static async Task BackupIntegrityVerifiesAndRepairsMissingObject()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("data.txt", "integrity-payload");
    var repository = Path.Combine(workspace.RootDirectory, "integrity-repository");
    var service = new BackupRepositoryService();
    var backup = await service.CreateBackupAsync(new BackupTaskRequest(
        workspace.SourceDirectory, repository, BackupMode.IncrementalHistory));
    var objectPath = Path.Combine(repository, backup.Snapshot.Files.Single().ObjectRelativePath);

    var healthy = await service.VerifyIntegrityAsync(repository);
    Assert.True(healthy.IsHealthy);

    File.Delete(objectPath);
    var unhealthy = await service.VerifyIntegrityAsync(repository);
    Assert.False(unhealthy.IsHealthy);
    Assert.Equal(1, unhealthy.MissingObjects);

    var repaired = await service.RepairAsync(repository, unhealthy);
    Assert.Equal(1, repaired);
    var verified = await service.VerifyIntegrityAsync(repository);
    Assert.True(verified.IsHealthy);
}

static async Task ImmutableSnapshotSurvivesRetention()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("data.txt", "version-one");
    var repository = Path.Combine(workspace.RootDirectory, "immutable-repository");
    var service = new BackupRepositoryService();
    var request = new BackupTaskRequest(workspace.SourceDirectory, repository, BackupMode.FullSnapshot);
    var first = await service.CreateBackupAsync(request);
    workspace.WriteSource("data.txt", "version-two-longer", DateTime.UtcNow.AddMinutes(1));
    var second = await service.CreateBackupAsync(request);

    await service.MarkImmutableAsync(repository, first.Snapshot.Id, DateTime.UtcNow.AddHours(1));
    var deleted = await service.ApplyRetentionAsync(repository, keepLatest: 1);
    var snapshots = await service.GetSnapshotsAsync(repository);

    Assert.Equal(0, deleted);
    Assert.Equal(2, snapshots.Count);
    Assert.True(snapshots.Any(snapshot => snapshot.Id == first.Snapshot.Id));
    Assert.True(snapshots.Any(snapshot => snapshot.Id == second.Snapshot.Id));

    foreach (var path in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(path, FileAttributes.Normal);
    }
}

static Task GfsRetentionKeepsPerBucket()
{
    var snapshots = new[]
    {
        Snapshot(2026, 6, 15),
        Snapshot(2026, 7, 15),
        Snapshot(2026, 8, 15),
        Snapshot(2026, 8, 20),
    };
    var daily = BackupRetentionPlanner.SelectRetained(snapshots, new BackupRetentionPolicy(KeepDaily: 2));
    Assert.Equal(2, daily.Count);
    Assert.True(daily.Contains(snapshots.Single(snapshot => snapshot.CreatedAtUtc.Month == 8 && snapshot.CreatedAtUtc.Day == 20).Id));
    Assert.True(daily.Contains(snapshots.Single(snapshot => snapshot.CreatedAtUtc.Month == 8 && snapshot.CreatedAtUtc.Day == 15).Id));

    var monthly = BackupRetentionPlanner.SelectRetained(snapshots, new BackupRetentionPolicy(KeepMonthly: 2));
    Assert.Equal(2, monthly.Count);
    Assert.True(monthly.Contains(snapshots.Single(snapshot => snapshot.CreatedAtUtc.Month == 8 && snapshot.CreatedAtUtc.Day == 20).Id));
    Assert.True(monthly.Contains(snapshots.Single(snapshot => snapshot.CreatedAtUtc.Month == 7).Id));

    var yearly = BackupRetentionPlanner.SelectRetained(snapshots, new BackupRetentionPolicy(KeepYearly: 1));
    Assert.Equal(1, yearly.Count);
    return Task.CompletedTask;
}

static BackupSnapshot Snapshot(int year, int month, int day) =>
    new(Guid.NewGuid(), new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
        "source", BackupMode.FullSnapshot, []);

static async Task RestoreDrillVerifiesSnapshot()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("a.txt", "drill-a");
    workspace.WriteSource(Path.Combine("nested", "b.txt"), "drill-b");
    var repository = Path.Combine(workspace.RootDirectory, "drill-repository");
    var service = new BackupRepositoryService();
    var backup = await service.CreateBackupAsync(new BackupTaskRequest(
        workspace.SourceDirectory, repository, BackupMode.FullSnapshot));
    var drillDirectory = Path.Combine(workspace.RootDirectory, "drill-restore");

    var result = await service.DrillRestoreAsync(repository, backup.Snapshot, drillDirectory);

    Assert.True(result.IsSuccessful);
    Assert.Equal(2, result.VerifiedFiles);
    Assert.Equal("drill-a", File.ReadAllText(Path.Combine(drillDirectory, "a.txt"), Encoding.UTF8));
    Assert.Equal("drill-b", File.ReadAllText(Path.Combine(drillDirectory, "nested", "b.txt"), Encoding.UTF8));
}

static Task AppStateManifestCapturesMachineInfo()
{
    var manifest = new AppStateManifestService().Capture();
    Assert.True(!string.IsNullOrWhiteSpace(manifest.MachineName));
    Assert.True(!string.IsNullOrWhiteSpace(manifest.OsDescription));
    Assert.NotNull(manifest.InstalledApplications);
    return Task.CompletedTask;
}

static async Task RunConditionEvaluatorEvaluatesConditions()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "condition-eval.db");
    var task = new SavedCopyTask(Guid.NewGuid(), "cond", workspace.Request(CopyMode.AccumulateSync), DateTime.UtcNow, DateTime.UtcNow);
    await repository.SaveTaskAsync(task);
    var evaluator = new RunConditionEvaluator();
    var existing = Path.Combine(workspace.RootDirectory, "exists.txt");
    await File.WriteAllTextAsync(existing, "x");

    var pass = await evaluator.EvaluateAsync(
        [new RunCondition(RunConditionKind.FileExists, existing),
         new RunCondition(RunConditionKind.MinimumDiskSpaceGb, "0.000001")],
        task, repository);
    Assert.True(pass.Passed);

    var fail = await evaluator.EvaluateAsync(
        [new RunCondition(RunConditionKind.FileExists, Path.Combine(workspace.RootDirectory, "missing.txt"))],
        task, repository);
    Assert.False(fail.Passed);
    Assert.True(fail.Reason!.Contains("前置文件不存在", StringComparison.Ordinal));
}

static async Task ScheduledRunSkipsWhenConditionFails()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("cond.txt", "payload");
    var repository = await CreateRepository(workspace, "condition.db");
    var task = new SavedCopyTask(Guid.NewGuid(), "cond-task", workspace.Request(CopyMode.AccumulateSync), DateTime.UtcNow, DateTime.UtcNow);
    await repository.SaveTaskAsync(task);
    var runner = new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository));
    var missing = Path.Combine(workspace.RootDirectory, "missing-marker.txt");

    var result = await runner.RunAsync(new TaskSchedule(
        task.Id, true, ScheduleTrigger.Daily, TimeSpan.Zero,
        Conditions: [new RunCondition(RunConditionKind.FileExists, missing)]));

    Assert.False(result.Executed);
    Assert.Equal("已跳过", result.Status);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "cond.txt")));
}

static async Task ScheduledRunSkipsOutsideTimeWindow()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("window.txt", "payload");
    var repository = await CreateRepository(workspace, "window.db");
    var task = new SavedCopyTask(Guid.NewGuid(), "windowed", workspace.Request(CopyMode.AccumulateSync), DateTime.UtcNow, DateTime.UtcNow);
    await repository.SaveTaskAsync(task);
    var runner = new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository));
    var now = DateTime.Now.TimeOfDay;
    var pastWindow = new TimeWindow(now.Subtract(TimeSpan.FromHours(2)), now.Subtract(TimeSpan.FromHours(1)));

    var result = await runner.RunAsync(new TaskSchedule(
        task.Id, true, ScheduleTrigger.Daily, TimeSpan.Zero, TimeWindow: pastWindow));

    Assert.False(result.Executed);
    Assert.Equal("已跳过", result.Status);
    Assert.False(File.Exists(Path.Combine(workspace.TargetDirectory, "window.txt")));
}

static async Task TaskChainRunsSuccessorWithOutputAsSource()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("payload.txt", "chained-content");
    var repository = await CreateRepository(workspace, "chain.db");
    var now = DateTime.UtcNow;
    var mid = Path.Combine(workspace.RootDirectory, "mid");
    var final = Path.Combine(workspace.RootDirectory, "final");
    var first = new SavedCopyTask(Guid.NewGuid(), "first", new CopyTaskRequest(
        workspace.SourceDirectory, mid, CopyMode.AccumulateSync), now, now);
    var second = new SavedCopyTask(Guid.NewGuid(), "second", new CopyTaskRequest(
        Path.Combine(workspace.RootDirectory, "unused"), final, CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(first);
    await repository.SaveTaskAsync(second);
    await repository.SaveTaskExecutionOptionsAsync(new TaskExecutionOptions(
        first.Id, NextTaskId: second.Id, ChainOutputAsSource: true));

    var runner = new ScheduledTaskRunner(repository, CreateSyncWorkflow(repository));
    var firstRun = await runner.RunAsync(new TaskSchedule(first.Id, true, ScheduleTrigger.Daily, TimeSpan.Zero));
    Assert.True(firstRun.Executed);

    var chainResult = await new TaskChainRunner(repository, runner).RunNextAsync(first.Id, succeeded: true);

    Assert.NotNull(chainResult);
    Assert.True(chainResult!.Executed);
    Assert.Equal("chained-content", File.ReadAllText(Path.Combine(final, "payload.txt"), Encoding.UTF8));
}

static Task AutoTriggerRuleNormalizesMinInterval()
{
    var rule = new AutoTriggerRule(Guid.NewGuid(), AutoTriggerKind.FileChange, true,
        TimeSpan.FromSeconds(2), MinIntervalBetweenRuns: TimeSpan.FromSeconds(999999));
    var normalized = rule.Normalize();
    Assert.Equal(TimeSpan.FromSeconds(86400), normalized.MinIntervalBetweenRuns);
    Assert.Equal(TimeSpan.FromSeconds(2), normalized.DebounceDelay);
    return Task.CompletedTask;
}

static async Task ScheduleConditionsAndWindowRoundTrip()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "schedule-extras.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "extras", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    var schedule = new TaskSchedule(task.Id, true, ScheduleTrigger.Daily, TimeSpan.FromHours(3),
        Conditions: [new RunCondition(RunConditionKind.FileExists, "C:\\marker.txt")],
        TimeWindow: new TimeWindow(TimeSpan.FromHours(1), TimeSpan.FromHours(2)));

    await repository.SaveScheduleAsync(schedule);
    var loaded = await repository.GetScheduleAsync(task.Id);

    Assert.NotNull(loaded);
    Assert.Equal(1, loaded!.EffectiveConditions.Count);
    Assert.Equal(RunConditionKind.FileExists, loaded.EffectiveConditions[0].Kind);
    Assert.NotNull(loaded.TimeWindow);
    Assert.Equal(TimeSpan.FromHours(1), loaded.TimeWindow!.StartLocalTime);
    Assert.Equal(TimeSpan.FromHours(2), loaded.TimeWindow.EndLocalTime);
}

static async Task EncryptedArchiveHidesFileNames()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("secret-plan-2026.txt", "top-secret");
    var archive = Path.Combine(workspace.RootDirectory, "enc.ctbackup");
    await new SecureBackupArchiveService().CreateAsync(
        workspace.SourceDirectory, archive, compress: true, password: "passphrase");
    var bytes = await File.ReadAllBytesAsync(archive);
    var needle = Encoding.ASCII.GetBytes("secret-plan-2026");
    Assert.False(ContainsSequence(bytes, needle), "加密包不应泄露原始文件名。");
}

static async Task ArchivePasswordRotationChangesKey()
{
    using var workspace = TestWorkspace.Create();
    workspace.WriteSource("doc.txt", "rotating-content");
    var archive = Path.Combine(workspace.RootDirectory, "rot.ctbackup");
    var service = new SecureBackupArchiveService();
    await service.CreateAsync(workspace.SourceDirectory, archive, password: "old-password");
    await service.ChangePasswordAsync(archive, "old-password", "new-password");

    await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
        service.RestoreAsync(archive, Path.Combine(workspace.RootDirectory, "bad"), "old-password"));
    var restore = Path.Combine(workspace.RootDirectory, "restore");
    await service.RestoreAsync(archive, restore, "new-password");
    Assert.Equal("rotating-content", File.ReadAllText(Path.Combine(restore, "doc.txt"), Encoding.UTF8));
}

static Task RecoveryCodeIsStrong()
{
    var code = SecureBackupArchiveService.GenerateRecoveryCode();
    Assert.True(code.Length >= 20);
    Assert.True(code.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
    return Task.CompletedTask;
}

static Task RansomwareChangeDetectorFlagsBurst()
{
    var detector = new RansomwareChangeDetector(thresholdCount: 10, window: TimeSpan.FromSeconds(5));
    var start = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
    var burst = Enumerable.Range(0, 10)
        .Select(index => new RansomwareChangeDetector.ChangeEvent($"f{index}.txt", start.AddMilliseconds(index * 100)))
        .ToArray();
    Assert.True(detector.Detect(burst));

    var sparse = Enumerable.Range(0, 5)
        .Select(index => new RansomwareChangeDetector.ChangeEvent($"s{index}.txt", start.AddSeconds(index * 60)))
        .ToArray();
    Assert.False(detector.Detect(sparse));
    return Task.CompletedTask;
}

static async Task AuditLogChainDetectsTampering()
{
    using var workspace = TestWorkspace.Create();
    var path = Path.Combine(workspace.RootDirectory, "audit.log");
    var chain = new AuditLogChain(path);
    await chain.AppendAsync("task.run", "run-1");
    await chain.AppendAsync("task.run", "run-2");
    await chain.AppendAsync("task.run", "run-3");
    Assert.True(await chain.VerifyAsync());

    var lines = (await File.ReadAllLinesAsync(path)).ToArray();
    lines[1] = lines[1].Replace("run-2", "run-2-tampered", StringComparison.Ordinal);
    await File.WriteAllLinesAsync(path, lines);
    Assert.False(await chain.VerifyAsync());
}

static bool ContainsSequence(byte[] haystack, byte[] needle)
{
    for (var start = 0; start <= haystack.Length - needle.Length; start++)
    {
        var match = true;
        for (var offset = 0; offset < needle.Length; offset++)
        {
            if (haystack[start + offset] != needle[offset])
            {
                match = false;
                break;
            }
        }

        if (match)
        {
            return true;
        }
    }

    return false;
}

static async Task AzureBlobAdapterSignsSharedKeyRequests()
{
    var requests = new List<(string Method, string Path, string Query, string? Authorization, string? MsDate)>();
    var handler = new TestHttpHandler(request =>
    {
        requests.Add((
            request.Method.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("x-ms-date", out var dates) ? dates.FirstOrDefault() : null));
        if (request.Method == HttpMethod.Get && request.RequestUri.Query.Contains("comp=list", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.XmlOk(
                "<EnumerationResults><Blobs><Blob><Name>root/a.txt</Name><Properties>"
                + "<Content-Length>5</Content-Length><Last-Modified>Thu, 13 Aug 2026 00:00:00 GMT</Last-Modified>"
                + "</Properties></Blob></Blobs></EnumerationResults>"));
        }

        if (request.Method == HttpMethod.Put)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Created));
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    });
    using var client = new HttpClient(handler);
    var accountKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-account-key"));
    await using var adapter = new AzureBlobStorageAdapter(
        "https://acct.blob.core.windows.net/", "acct", accountKey, "container", "root", client);

    var items = await adapter.ListAsync(recursive: true);
    await using var upload = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
    await adapter.WriteAsync("b.txt", upload, upload.Length);

    Assert.Equal("a.txt", items.Single().RelativePath);
    Assert.True(requests.All(request => request.Authorization?.StartsWith("SharedKey acct:", StringComparison.Ordinal) == true));
    Assert.True(requests.All(request => !string.IsNullOrWhiteSpace(request.MsDate)));
    Assert.True(requests.Any(request => request.Path.Contains("container/root/", StringComparison.Ordinal)));
    Assert.True(requests.All(request => !request.Authorization!.Contains("test-account-key", StringComparison.Ordinal)));
}

static async Task GenericJsonCloudAdapterShapesBearerRequests()
{
    var requests = new List<(string Method, string Path, string? Authorization)>();
    var handler = new TestHttpHandler(request =>
    {
        requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
        if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/list", StringComparison.Ordinal))
        {
            return Task.FromResult(TestHttpHandler.Json(
                "{\"items\":[{\"path\":\"root/file.txt\",\"isDirectory\":false,\"size\":7,\"modifiedUtc\":\"2026-08-13T00:00:00Z\"}]}"));
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    });
    using var client = new HttpClient(handler);
    await using var adapter = new GenericJsonCloudStorageAdapter("https://cloud.test/api/", "token-123", "root", client);

    var items = await adapter.ListAsync();

    Assert.Equal("file.txt", items.Single().RelativePath);
    Assert.True(requests.All(request => request.Authorization == "Bearer token-123"));
    Assert.True(requests.All(request => request.Path.Contains("/list", StringComparison.Ordinal)));
}

static async Task LanFileTransferRoundTripsWithCompression()
{
    var payload = Encoding.UTF8.GetBytes(new string('x', 64 * 1024));
    using var buffer = new MemoryStream();
    await LanFileTransfer.SendAsync(buffer, "folder/data.bin", new MemoryStream(payload), compress: true);
    buffer.Position = 0;
    var (header, content) = await LanFileTransfer.ReceiveAsync(buffer);
    var decoded = LanFileTransfer.DecodeContent(header, content);

    Assert.Equal("folder/data.bin", header.RelativePath);
    Assert.True(header.Compressed);
    Assert.True(payload.AsSpan().SequenceEqual(decoded));
}

static Task TimeBasedSpeedLimiterResolvesSchedule()
{
    var schedule = new SpeedSchedule(
    [
        new SpeedScheduleEntry(TimeSpan.FromHours(9), TimeSpan.FromHours(18), 5L * 1024 * 1024),
        new SpeedScheduleEntry(TimeSpan.FromHours(18), TimeSpan.FromHours(9), 50L * 1024 * 1024),
    ]);
    var limiter = new TimeBasedSpeedLimiter();
    Assert.Equal(5L * 1024 * 1024, limiter.ResolveBytesPerSecond(schedule, TimeSpan.FromHours(12)));
    Assert.Equal(50L * 1024 * 1024, limiter.ResolveBytesPerSecond(schedule, TimeSpan.FromHours(20)));
    Assert.Equal(50L * 1024 * 1024, limiter.ResolveBytesPerSecond(schedule, TimeSpan.FromHours(2)));
    Assert.True(limiter.ResolveBytesPerSecond(SpeedSchedule.Empty, TimeSpan.FromHours(12)) is null);
    return Task.CompletedTask;
}

static async Task FactoryMapsMinioToS3Compatible()
{
    using var workspace = TestWorkspace.Create();
    var credentials = new WindowsCredentialStore(Path.Combine(workspace.RootDirectory, "credentials"));
    credentials.Save("CopyTools/Storage/minio", "AK", "SK");
    var factory = new StorageAdapterFactory(credentials);
    var profile = StorageConnectionProfile.Create(
        "minio", StorageConnectionKind.Minio, "https://s3.test/",
        userName: "AK", region: "us-east-1", bucket: "bucket")
        with { CredentialTarget = "CopyTools/Storage/minio" };

    await using var adapter = factory.Create(profile);

    Assert.True(adapter.DisplayName.Contains("MinIO", StringComparison.Ordinal));
}

static async Task ImNotificationChannelsGenerateDistinctBodies()
{
    using var workspace = TestWorkspace.Create();
    var repository = new SqliteCopyToolsRepository(Path.Combine(workspace.RootDirectory, "im-notify.db"));
    await repository.InitializeAsync();
    var credentials = new WindowsCredentialStore(Path.Combine(workspace.RootDirectory, "credentials"));
    var bodies = new List<string>();
    var handler = new TestHttpHandler(async request =>
    {
        bodies.Add(await request.Content!.ReadAsStringAsync());
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    });
    using var client = new HttpClient(handler);
    var service = new TaskNotificationService(repository, credentials, client);
    var notification = new TaskResultNotification(Guid.NewGuid(), "IM 任务", "成功", DateTime.UtcNow, 1, 0, 0);

    await service.SendChannelAsync(new NotificationChannel(Guid.NewGuid(), "ding", NotificationChannelKind.DingTalk, true,
        "https://oapi.dingtalk.test/robot/send", "", "CopyTools/im/ding", "", ""), notification);
    await service.SendChannelAsync(new NotificationChannel(Guid.NewGuid(), "feishu", NotificationChannelKind.Feishu, true,
        "https://open.feishu.test/hook", "", "CopyTools/im/feishu", "", ""), notification);
    await service.SendChannelAsync(new NotificationChannel(Guid.NewGuid(), "slack", NotificationChannelKind.Slack, true,
        "https://hooks.slack.test/services/x", "", "CopyTools/im/slack", "", ""), notification);

    Assert.Equal(3, bodies.Count);
    Assert.True(bodies[0].Contains("msgtype", StringComparison.Ordinal) && bodies[0].Contains("text", StringComparison.Ordinal));
    Assert.True(bodies[1].Contains("msg_type", StringComparison.Ordinal));
    Assert.True(bodies[2].Contains("IM 任务", StringComparison.Ordinal));
}

static Task ShellIntegrationBuildsEntries()
{
    var service = new ShellIntegrationService();
    var entries = service.BuildEntries(@"C:\Program Files\CopyTools\CopyTools.exe");

    Assert.Equal(2, entries.Count);
    Assert.True(entries.All(entry => entry.Command.Contains("\"%1\"", StringComparison.Ordinal)));
    Assert.True(entries.All(entry => entry.Command.Contains(
        "\"C:\\Program Files\\CopyTools\\CopyTools.exe\"", StringComparison.Ordinal)));
    Assert.True(entries.Any(entry => entry.RegistryPath.Contains("Directory", StringComparison.Ordinal)));
    Assert.True(entries.Any(entry => entry.RegistryPath.Contains("*", StringComparison.Ordinal)));
    return Task.CompletedTask;
}

static async Task DirectoryDiffServiceComparesTrees()
{
    using var workspace = TestWorkspace.Create();
    var left = Path.Combine(workspace.RootDirectory, "left");
    var right = Path.Combine(workspace.RootDirectory, "right");
    Directory.CreateDirectory(left);
    Directory.CreateDirectory(right);
    var timestamp = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
    File.WriteAllText(Path.Combine(left, "same.txt"), "same");
    File.SetLastWriteTimeUtc(Path.Combine(left, "same.txt"), timestamp);
    File.WriteAllText(Path.Combine(right, "same.txt"), "same");
    File.SetLastWriteTimeUtc(Path.Combine(right, "same.txt"), timestamp);
    File.WriteAllText(Path.Combine(left, "only-left.txt"), "left");
    File.WriteAllText(Path.Combine(right, "only-right.txt"), "right");
    File.WriteAllText(Path.Combine(left, "changed.txt"), "left-version");
    File.WriteAllText(Path.Combine(right, "changed.txt"), "right-version");

    var rows = await new DirectoryDiffService().CompareAsync(left, right);

    Assert.Equal(4, rows.Count);
    Assert.Equal(DiffSide.Identical, rows.Single(row => row.RelativePath == "same.txt").Side);
    Assert.Equal(DiffSide.LeftOnly, rows.Single(row => row.RelativePath == "only-left.txt").Side);
    Assert.Equal(DiffSide.RightOnly, rows.Single(row => row.RelativePath == "only-right.txt").Side);
    Assert.Equal(DiffSide.BothChanged, rows.Single(row => row.RelativePath == "changed.txt").Side);
}

static async Task DashboardServiceAggregatesHistory()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "dashboard.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "dash", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    await repository.SaveScheduleAsync(new TaskSchedule(task.Id, true, ScheduleTrigger.Daily, TimeSpan.FromHours(3)));
    await repository.SaveRunAsync(new RunHistoryEntry(
        Guid.NewGuid(), task.Id, task.Name, task.Request, now.AddSeconds(-4), now.AddSeconds(-3), "成功", 2, 0, 0, 100, []));
    await repository.SaveRunAsync(new RunHistoryEntry(
        Guid.NewGuid(), task.Id, task.Name, task.Request, now.AddSeconds(-2), now.AddSeconds(-1), "失败", 0, 0, 1, 0, []));

    var summary = await new DashboardService(repository).BuildAsync();

    Assert.Equal(2, summary.TotalRuns);
    Assert.Equal(1, summary.SuccessfulRuns);
    Assert.Equal(1, summary.FailedRuns);
    Assert.Equal(50.0, summary.SuccessRate);
    Assert.Equal(100L, summary.TotalCopiedBytes);
    Assert.Equal(1, summary.ActiveTasks);
    Assert.Equal(1, summary.ScheduledTasks);
}

static async Task GlobalSearchServiceFindsAcrossEntities()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "search.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "季度报表备份", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    await repository.SaveRunAsync(new RunHistoryEntry(
        Guid.NewGuid(), task.Id, task.Name, task.Request, now, now, "成功", 1, 0, 0, 5,
        [new RunHistoryItem("季度报表/2026.xlsx", CopyAction.CopyNew, "成功", 5, null, 1)]));

    var hits = await new GlobalSearchService(repository).SearchAsync("季度报表");

    Assert.True(hits.Count >= 2);
    Assert.True(hits.Any(hit => hit.Kind == "任务"));
    Assert.True(hits.Any(hit => hit.Kind == "运行"));
}

static Task NotificationCenterBuffersAndMarksRead()
{
    var center = new NotificationCenter(capacity: 3);
    center.Push("a", "first", NotificationSeverity.Info);
    center.Push("b", "second", NotificationSeverity.Warning);
    center.Push("c", "third", NotificationSeverity.Error);
    center.Push("d", "fourth", NotificationSeverity.Success);

    Assert.Equal(3, center.GetNotifications().Count);
    Assert.Equal(3, center.UnreadCount);
    center.MarkAllRead();
    Assert.Equal(0, center.UnreadCount);
    Assert.Equal(0, center.GetNotifications(unreadOnly: true).Count);
    return Task.CompletedTask;
}

static Task LocalizationServiceResolvesLanguage()
{
    var localization = new LocalizationService();
    Assert.Equal("开始执行", localization.Get("common.execute"));
    Assert.Equal("Run", localization.Get("common.execute", "en-US"));
    Assert.Equal("新建复制任务", localization.Get("nav.newCopyTask"));
    Assert.Equal("New Copy Task", localization.Get("nav.newCopyTask", "en-US"));
    Assert.Equal("missing.key", localization.Get("missing.key", "en-US"));
    return Task.CompletedTask;
}

static async Task ParallelScanComputesAllHashes()
{
    using var workspace = TestWorkspace.Create();
    for (var index = 0; index < 8; index++)
    {
        workspace.WriteSource($"f{index}.bin", new string((char)('a' + index), 100));
    }

    var files = await new DirectoryScanner().ScanAsync(
        workspace.SourceDirectory, new FileFilterOptions(), CancellationToken.None, ContentHashMode.Sha256);

    Assert.Equal(8, files.Count);
    Assert.True(files.All(file => !string.IsNullOrEmpty(file.ContentHash)));
}

static async Task HistoryArchiveExportsAndCleans()
{
    using var workspace = TestWorkspace.Create();
    var repository = await CreateRepository(workspace, "archive.db");
    var now = DateTime.UtcNow;
    var task = new SavedCopyTask(Guid.NewGuid(), "archive-task", workspace.Request(CopyMode.AccumulateSync), now, now);
    await repository.SaveTaskAsync(task);
    await repository.SaveRunAsync(new RunHistoryEntry(
        Guid.NewGuid(), task.Id, task.Name, task.Request, now.AddDays(-3), now.AddDays(-3), "成功", 1, 0, 0, 1, []));
    await repository.SaveRunAsync(new RunHistoryEntry(
        Guid.NewGuid(), task.Id, task.Name, task.Request, now.AddDays(-2), now.AddDays(-2), "成功", 1, 0, 0, 1, []));
    await repository.SaveRunAsync(new RunHistoryEntry(
        Guid.NewGuid(), task.Id, task.Name, task.Request, now, now, "成功", 1, 0, 0, 1, []));

    var archivePath = Path.Combine(workspace.RootDirectory, "archive.json");
    var archived = await new HistoryArchiveService().ArchiveAsync(repository, now.AddDays(-1), archivePath);

    Assert.Equal(2, archived);
    Assert.Equal(1, (await repository.GetRecentRunsAsync()).Count);
    Assert.True((await File.ReadAllTextAsync(archivePath)).Contains("archive-task", StringComparison.Ordinal));
}

static Task LargeListPagerPaginates()
{
    var pager = new LargeListPager<int>(Enumerable.Range(0, 10).ToArray(), 4);
    Assert.Equal(10, pager.TotalCount);
    Assert.Equal(3, pager.PageCount);
    Assert.Equal(4, pager.GetPage(0).Count);
    Assert.Equal(2, pager.GetPage(2).Count);
    Assert.Equal(8, pager.GetPage(99)[0]);

    var empty = new LargeListPager<int>([], 4);
    Assert.Equal(0, empty.PageCount);
    Assert.Equal(0, empty.GetPage(0).Count);
    return Task.CompletedTask;
}

static Task FailureAggregationGroupsByCategory()
{
    var now = DateTime.UtcNow;
    var runs = new[]
    {
        new RunHistoryEntry(Guid.NewGuid(), null, "a", new CopyTaskRequest("s", "t", CopyMode.FullCopy), now, now, "失败", 0, 0, 2, 0,
            [new RunHistoryItem("a.txt", CopyAction.CopyNew, "失败", 0, "访问被拒绝", 3),
             new RunHistoryItem("b.txt", CopyAction.CopyNew, "失败", 0, "访问被拒绝", 3)]),
        new RunHistoryEntry(Guid.NewGuid(), null, "b", new CopyTaskRequest("s", "t", CopyMode.FullCopy), now, now, "失败", 0, 0, 1, 0,
            [new RunHistoryItem("c.txt", CopyAction.CopyNew, "失败", 0, "磁盘空间不足", 3)]),
    };

    var causes = new FailureAggregationService().Aggregate(runs);

    Assert.Equal(2, causes.Count);
    Assert.Equal("权限不足", causes[0].Category);
    Assert.Equal(2, causes[0].Count);
    Assert.Equal("磁盘空间不足", causes[1].Category);
    Assert.Equal(1, causes[1].Count);
    return Task.CompletedTask;
}

static Task StorageTrendPredictsDiskExhaustion()
{
    var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    var samples = new[]
    {
        new StorageSample(start, 100),
        new StorageSample(start.AddDays(10), 200),
    };

    var prediction = new StorageTrendService().Predict(samples, availableBytes: 1000, horizonDays: 30);

    Assert.False(prediction.InsufficientData);
    Assert.Equal(10.0, prediction.AverageBytesPerDay);
    Assert.Equal(300L, prediction.ProjectedBytesInDays);
    Assert.Equal(100, prediction.DaysUntilFull);
    return Task.CompletedTask;
}

static Task HealthScoreRatesOverallHealth()
{
    var service = new HealthScoreService();
    var healthy = service.Evaluate(100, DateTime.UtcNow, 0, 0);
    Assert.Equal(100, healthy.Score);
    Assert.Equal("优秀", healthy.Grade);
    Assert.Equal(0, healthy.Concerns.Count);

    var unhealthy = service.Evaluate(80, null, 500, 0.5);
    Assert.True(unhealthy.Score < 60);
    Assert.True(unhealthy.Concerns.Count >= 3);
    return Task.CompletedTask;
}

static async Task OllamaAssistantUsesChatEndpoint()
{
    string? body = null;
    string? path = null;
    var handler = new TestHttpHandler(async request =>
    {
        path = request.RequestUri!.AbsolutePath;
        body = await request.Content!.ReadAsStringAsync();
        return TestHttpHandler.Json("{\"message\":{\"content\":\"你好\"}}");
    });
    using var client = new HttpClient(handler);
    using var assistant = new OllamaAssistant("https://ollama.test/", "llama3", client);

    var text = await assistant.CompleteAsync("sys", "user");

    Assert.Equal("你好", text);
    Assert.True(path!.EndsWith("/api/chat", StringComparison.Ordinal));
    Assert.True(body!.Contains("\"model\":\"llama3\"", StringComparison.Ordinal));
    Assert.True(body.Contains("\"stream\":false", StringComparison.Ordinal));
}

static async Task OpenAiAssistantUsesBearerAndJsonMode()
{
    string? body = null;
    string? auth = null;
    string? path = null;
    var handler = new TestHttpHandler(async request =>
    {
        path = request.RequestUri!.AbsolutePath;
        auth = request.Headers.Authorization?.ToString();
        body = await request.Content!.ReadAsStringAsync();
        var content = "{\"name\":\"x\",\"value\":42}";
        var response = System.Text.Json.JsonSerializer.Serialize(
            new { choices = new[] { new { message = new { content } } } });
        return TestHttpHandler.Json(response);
    });
    using var client = new HttpClient(handler);
    using var assistant = new OpenAiCompatibleAssistant("https://api.test/v1/", "gpt-4o-mini", "sk-test", client);

    var parsed = await assistant.CompleteJsonAsync<AiSample>("sys", "user");

    Assert.NotNull(parsed);
    Assert.Equal("x", parsed!.Name);
    Assert.Equal(42, parsed.Value);
    Assert.Equal("Bearer sk-test", auth);
    Assert.True(path!.EndsWith("/chat/completions", StringComparison.Ordinal));
    Assert.True(body!.Contains("response_format", StringComparison.Ordinal));
}

static Task AiJsonExtractsFromMarkdownFences()
{
    var json = AiJson.Parse<AiSample>("```json\n{\"name\":\"x\",\"value\":42}\n```");
    Assert.NotNull(json);
    Assert.Equal("x", json!.Name);
    Assert.Equal(42, json.Value);
    Assert.True(AiJson.Parse<AiSample>("这不是 JSON") is null);
    return Task.CompletedTask;
}

static Task AiAssistantFactoryFallsBackToRule()
{
    var factory = new AiAssistantFactory();
    var rule = factory.Create(new AiProviderConfig(AiProviderKind.Rule));
    Assert.False(rule.IsAvailable);
    Assert.Equal("rule", rule.ProviderName);

    using var ollama = (OllamaAssistant)factory.Create(
        new AiProviderConfig(AiProviderKind.Ollama, "https://ollama.test/", "llama3"));
    Assert.Equal("ollama", ollama.ProviderName);
    return Task.CompletedTask;
}

static async Task NaturalLanguageTaskParsesDescription()
{
    var draft = await new NaturalLanguageTaskService().ParseAsync("把 D:\\照片 镜像同步到 E:\\备份，排除 *.tmp");
    Assert.Equal("D:\\照片", draft.SourceDirectory);
    Assert.Equal("E:\\备份", draft.TargetDirectory);
    Assert.Equal(CopyMode.MirrorSync, draft.Mode);
    Assert.True(draft.ExcludePatterns!.Contains("*.tmp"));
}

static async Task NaturalLanguageTaskUsesAssistantJson()
{
    var assistant = new StubAssistant
    {
        Json = _ => "{\"sourceDirectory\":\"C:\\\\in\",\"targetDirectory\":\"D:\\\\out\",\"mode\":\"MirrorSync\",\"confidence\":0.9}",
    };
    var draft = await new NaturalLanguageTaskService().ParseAsync("任意描述", assistant);

    Assert.Equal("C:\\in", draft.SourceDirectory);
    Assert.Equal("D:\\out", draft.TargetDirectory);
    Assert.Equal(CopyMode.MirrorSync, draft.Mode);
    Assert.Equal(0.9, draft.Confidence);
}

static Task SyncModeAdvisorExplainsConsequences()
{
    var advisor = new SyncModeAdvisor();
    var mirror = advisor.Advise("我要让两个目录保持一致");
    Assert.Equal(CopyMode.MirrorSync, mirror.Mode);
    Assert.True(mirror.Destructive);

    var incremental = advisor.Advise("增量复制");
    Assert.Equal(CopyMode.IncrementalCopy, incremental.Mode);
    Assert.False(incremental.Destructive);
    return Task.CompletedTask;
}

static async Task NaturalLanguageFilterParsesRules()
{
    var filters = await new NaturalLanguageFilterService().ParseAsync("排除 *.tmp 和 node_modules，排除目录 cache，大于 5 MB");

    Assert.True(filters.EffectiveExcludePatterns.Contains("*.tmp"));
    Assert.True(filters.EffectiveExcludePatterns.Contains("node_modules"));
    Assert.True(filters.EffectiveExcludedDirectories.Contains("cache"));
    Assert.Equal(5L * 1024 * 1024, filters.MinimumBytes);
}

static async Task HelpAssistantAnswersFromFaq()
{
    var answer = await new HelpAssistant().AskAsync("镜像同步会不会删除我的文件？");
    Assert.True(answer.Found);
    Assert.Equal("faq", answer.Source);
    Assert.True(answer.Answer.Contains("隔离区", StringComparison.Ordinal));

    var missing = await new HelpAssistant().AskAsync("xyzzy");
    Assert.False(missing.Found);
}

static async Task RiskExplanationSummarizesPlan()
{
    var request = new CopyTaskRequest("s", "t", CopyMode.MirrorSync);
    var plan = new CopyPlan(request,
    [
        new CopyPlanItem("a.txt", "s\\a.txt", "t\\a.txt", 5, DateTime.UtcNow, CopyAction.CopyNew, "new"),
        new CopyPlanItem("extra.txt", "t\\extra.txt", "t\\extra.txt", 5, DateTime.UtcNow, CopyAction.QuarantineTarget, "extra"),
    ], DateTime.UtcNow);

    var explanation = await new RiskExplanationService().ExplainAsync(plan);

    Assert.Equal("高", explanation.RiskLevel);
    Assert.Equal(1, explanation.QuarantineFiles);
    Assert.Equal(1, explanation.NewFiles);
    Assert.True(explanation.Narrative.Contains("隔离", StringComparison.Ordinal));
}

static Task ConflictAdvisorRecommendsByMetadata()
{
    var advisor = new ConflictAdvisor();
    var baseTime = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
    var sourceWins = advisor.Advise("a.txt", 10, baseTime.AddMinutes(30), 5, baseTime);
    Assert.Equal(ConflictResolutionPolicy.SourceWins, sourceWins.Recommendation);
    var targetWins = advisor.Advise("a.txt", 5, baseTime, 10, baseTime.AddMinutes(30));
    Assert.Equal(ConflictResolutionPolicy.TargetWins, targetWins.Recommendation);
    var keepBoth = advisor.Advise("a.txt", 5, baseTime, 5, baseTime);
    Assert.Equal(ConflictResolutionPolicy.KeepBoth, keepBoth.Recommendation);
    return Task.CompletedTask;
}

static async Task FailureRootCauseProducesAdvice()
{
    var now = DateTime.UtcNow;
    var runs = new[]
    {
        new RunHistoryEntry(Guid.NewGuid(), null, "t", new CopyTaskRequest("s", "t", CopyMode.FullCopy), now, now, "失败", 0, 0, 1, 0,
            [new RunHistoryItem("x.txt", CopyAction.CopyNew, "失败", 0, "访问被拒绝", 3)]),
    };

    var analysis = await new FailureRootCauseService().AnalyzeAsync(runs);

    Assert.Equal("权限不足", analysis.Causes[0].Category);
    Assert.True(analysis.Advice.Contains("权限", StringComparison.Ordinal));
}

static Task RetryAdvisorSuggestsNetworkRetry()
{
    var network = new RetryAdvisor().Advise("网络连接失败");
    Assert.Equal(5, network.MaxAttempts);
    Assert.NotNull(network.AvailabilityWait);

    var permission = new RetryAdvisor().Advise("访问被拒绝");
    Assert.Equal(1, permission.MaxAttempts);
    return Task.CompletedTask;
}

static Task FileClassifierClassifiesByExtension()
{
    var classifier = new FileClassifier();
    Assert.Equal("图片", classifier.Classify("a.jpg").Category);
    Assert.Equal("文档", classifier.Classify("a.pdf").Category);
    Assert.Equal("其他", classifier.Classify("a.unknown").Category);
    return Task.CompletedTask;
}

static Task SimilarFileDetectorGroupsByNameAndSize()
{
    using var workspace = TestWorkspace.Create();
    File.WriteAllText(Path.Combine(workspace.RootDirectory, "photo.jpg"), "aaaa");
    File.WriteAllText(Path.Combine(workspace.RootDirectory, "photo (1).jpg"), "bbbb");
    File.WriteAllText(Path.Combine(workspace.RootDirectory, "one.txt"), "123");
    File.WriteAllText(Path.Combine(workspace.RootDirectory, "two.txt"), "456");

    var groups = new SimilarFileDetector().Detect(Directory.EnumerateFiles(workspace.RootDirectory).ToArray());

    Assert.True(groups.Any(group => group.Reason == "相似名称" && group.Paths.Count == 2));
    Assert.True(groups.Any(group => group.Reason == "相同大小与类型" && group.Paths.Count == 2));
    return Task.CompletedTask;
}

static Task SensitiveFileScannerFlagsSecrets()
{
    var scanner = new SensitiveFileScanner();
    Assert.True(scanner.Scan(".env").IsSensitive);
    Assert.True(scanner.Scan("id_rsa").IsSensitive);
    Assert.True(scanner.Scan("身份证扫描件.jpg").IsSensitive);
    Assert.False(scanner.Scan("photo.jpg").IsSensitive);
    return Task.CompletedTask;
}

static Task ArchiveOrganizerSuggestsCategories()
{
    var suggestions = new ArchiveOrganizer().Suggest(["a.jpg", "b.pdf", "c.mp4"]);

    Assert.True(suggestions.Single(s => s.RelativePath == "a.jpg").SuggestedDirectory.Contains("图片"));
    Assert.True(suggestions.Single(s => s.RelativePath == "b.pdf").SuggestedDirectory.Contains("文档"));
    Assert.True(suggestions.Single(s => s.RelativePath == "c.mp4").SuggestedDirectory.Contains("视频"));
    return Task.CompletedTask;
}

static async Task SpaceAdvisorWarnsAboutDiskFull()
{
    var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    var samples = new[] { new StorageSample(start, 100), new StorageSample(start.AddDays(10), 200) };
    var advice = await new SpaceAdvisor().AdviseAsync(1000, samples);
    Assert.Equal(100, advice.Trend.DaysUntilFull);
    Assert.True(advice.Narrative.Contains("100 天", StringComparison.Ordinal));
}

static async Task HealthReportNarratesScore()
{
    var score = new HealthScoreReport(100, "优秀", []);
    var summary = new DashboardSummary(10, 10, 0, 0, 100.0, 5000, 1, 1, 0, 0, DateTime.UtcNow);
    var report = await new HealthReportService().BuildAsync(score, summary);
    Assert.True(report.Narrative.Contains("健康评分 100", StringComparison.Ordinal));
}

static Task ScheduleAdvisorAvoidsFailureHour()
{
    var now = new DateTime(2026, 8, 15, 3, 0, 0, DateTimeKind.Utc);
    var runs = new[]
    {
        new RunHistoryEntry(Guid.NewGuid(), null, "t", new CopyTaskRequest("s", "t", CopyMode.FullCopy),
            now, now, "失败", 0, 0, 1, 0, []),
    };
    var advice = new ScheduleAdvisor().Advise(runs);
    Assert.NotNull(advice.SuggestedWindow);
    Assert.True(advice.Reason.Contains("3 点", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task DurationEstimatorEstimatesFromThroughput()
{
    var now = DateTime.UtcNow;
    var runs = new[]
    {
        new RunHistoryEntry(Guid.NewGuid(), null, "t", new CopyTaskRequest("s", "t", CopyMode.FullCopy),
            now.AddSeconds(-10), now, "成功", 1, 0, 0, 1000, []),
    };
    var estimate = new DurationEstimator().Estimate(runs, 500);
    Assert.False(estimate.InsufficientData);
    Assert.Equal(TimeSpan.FromSeconds(5), estimate.Estimated);
    return Task.CompletedTask;
}

static Task AnomalyDetectorFlagsSpike()
{
    var report = new AnomalyDetector().Detect([1, 1, 1, 1, 50]);
    Assert.True(report.IsAnomalous);
    Assert.True(report.Score >= 3);
    var normal = new AnomalyDetector().Detect([1, 1, 1, 1, 2]);
    Assert.False(normal.IsAnomalous);
    return Task.CompletedTask;
}

static async Task DrillInterpreterReportsRecoverability()
{
    var healthy = await new DrillInterpreter().InterpretAsync(new RestoreDrillResult(Guid.NewGuid(), 5, []));
    Assert.True(healthy.Narrative.Contains("通过", StringComparison.Ordinal));

    var failed = await new DrillInterpreter().InterpretAsync(new RestoreDrillResult(Guid.NewGuid(), 4, ["a.txt"]));
    Assert.True(failed.Narrative.Contains("失败", StringComparison.Ordinal));
}

static Task TransferAdvisorRecommendsDeltaForLargeRemote()
{
    var advice = new TransferAdvisor().Advise(64L * 1024 * 1024, isRemoteTarget: true, isManySmallFiles: false);
    Assert.True(advice.UseDelta);
    Assert.True(advice.Compress);
    Assert.NotNull(advice.SpeedLimitBytesPerSecond);

    var local = new TransferAdvisor().Advise(1024, isRemoteTarget: false, isManySmallFiles: true);
    Assert.False(local.UseDelta);
    Assert.Equal(4, local.MaxParallel);
    return Task.CompletedTask;
}

file sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        SourceDirectory = Path.Combine(rootDirectory, "source");
        TargetDirectory = Path.Combine(rootDirectory, "target");
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(TargetDirectory);
    }

    public string RootDirectory { get; }

    public string SourceDirectory { get; }

    public string TargetDirectory { get; }

    public static TestWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), $"copytools-tests-{Guid.NewGuid():N}");
        return new TestWorkspace(root);
    }

    public void WriteSource(string relativePath, string content, DateTime? lastWriteTimeUtc = null) =>
        WriteFile(SourceDirectory, relativePath, content, lastWriteTimeUtc);

    public void WriteTarget(string relativePath, string content, DateTime? lastWriteTimeUtc = null) =>
        WriteFile(TargetDirectory, relativePath, content, lastWriteTimeUtc);

    public string ReadTarget(string relativePath) =>
        File.ReadAllText(Path.Combine(TargetDirectory, relativePath), Encoding.UTF8);

    public CopyTaskRequest Request(CopyMode mode) =>
        new(SourceDirectory, TargetDirectory, mode);

    public Task<CopyPlan> CreatePlan(CopyMode mode) =>
        new CopyPlanService().CreatePlanAsync(Request(mode));

    public void Dispose()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void WriteFile(
        string root,
        string relativePath,
        string content,
        DateTime? lastWriteTimeUtc)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
        if (lastWriteTimeUtc is not null)
        {
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc.Value);
        }
    }
}

file sealed class InlineTestProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

file sealed class TestHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => handler(request);

    public static HttpResponseMessage Xml(string value) => new(System.Net.HttpStatusCode.MultiStatus)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/xml"),
    };

    public static HttpResponseMessage Json(string value) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage XmlOk(string value) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/xml"),
    };
}

file sealed class GeneratedStream(long length) : Stream
{
    private long _position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => _position = Math.Clamp(value, 0, length); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = length - _position;
        var read = (int)Math.Min(count, remaining);
        Array.Fill(buffer, (byte)'x', offset, read);
        _position += read;
        return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = (int)Math.Min(buffer.Length, length - _position);
        buffer.Span[..read].Fill((byte)'x');
        _position += read;
        return ValueTask.FromResult(read);
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, _ => length + offset };
        return Position;
    }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
}

file sealed record AiSample(string Name, int Value);

file sealed class StubAssistant : IAiAssistant
{
    public string ProviderName => "stub";
    public bool IsAvailable => true;
    public Func<string, string> Text { get; init; } = _ => "";
    public Func<string, string?> Json { get; init; } = _ => null;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(Text(userPrompt));

    public Task<T?> CompleteJsonAsync<T>(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var json = Json(userPrompt);
        return Task.FromResult(json is null ? default : AiJson.Parse<T>(json));
    }
}

public sealed class SampleCopyToolsPlugin : ICopyToolsPlugin
{
    public string Id => "copytools.tests.sample";
    public string Name => "测试插件";
    public Version ApiVersion => new(1, 0);
    public IReadOnlyCollection<IPluginStorageAdapterFactory> CreateStorageAdapterFactories() => [new SampleStorageFactory()];
    public IReadOnlyCollection<IFileVerificationProvider> CreateVerificationProviders() => [new EqualStreamVerifier()];
}

public sealed class SampleStorageFactory : IPluginStorageAdapterFactory
{
    public string Id => "tests.memory";
    public string DisplayName => "测试内存存储";
    public IStorageAdapter Create(StorageConnectionProfile profile, string userName, string secret) => new EmptyTestStorageAdapter();
}

public sealed class EqualStreamVerifier : IFileVerificationProvider
{
    public string Id => "tests.equal";
    public string DisplayName => "逐字节相等测试";
    public async Task<bool> VerifyAsync(Stream source, Stream target, CancellationToken cancellationToken = default)
    {
        var left = new byte[8192]; var right = new byte[8192];
        while (true)
        {
            var leftRead = await source.ReadAsync(left, cancellationToken);
            var rightRead = await target.ReadAsync(right, cancellationToken);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            if (!left.AsSpan(0, leftRead).SequenceEqual(right.AsSpan(0, rightRead))) return false;
        }
    }
}

public sealed class EmptyTestStorageAdapter : IStorageAdapter
{
    public string DisplayName => "测试内存存储";
    public StorageCapabilities Capabilities => StorageCapabilities.List;
    public Task<IReadOnlyList<StorageItem>> ListAsync(string relativeDirectory = "", bool recursive = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StorageItem>>([]);
    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task WriteAsync(string relativePath, Stream content, long? length = null, bool overwrite = true, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task MoveAsync(string sourceRelativePath, string targetRelativePath, bool overwrite = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file static class Assert
{
    public static void True(bool condition, string message = "预期条件为 true。")
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message = "预期条件为 false。") =>
        True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"预期：{expected}；实际：{actual}。");
        }
    }

    public static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("预期值不为 null。");
        }
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
    }
}
