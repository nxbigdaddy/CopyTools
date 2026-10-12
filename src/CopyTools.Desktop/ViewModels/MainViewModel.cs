using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using CopyTools.Application;
using CopyTools.Desktop.Infrastructure;
using CopyTools.Domain;
using CopyTools.Engine;
using Microsoft.Win32;
using CopyTools.Infrastructure;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CopyTools.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CopyWorkflow _workflow;
    private readonly IFolderPicker _folderPicker;
    private readonly ICopyToolsRepository _repository;
    private readonly QuarantineWorkflow _quarantineWorkflow;
    private readonly ScheduledTaskRunner _scheduledTaskRunner;
    private readonly ScheduleCalculator _scheduleCalculator = new();
    private readonly TaskTransferService _taskTransferService;
    private readonly ConfigurationBackupService _configurationBackupService;
    private readonly RunLogExportService _runLogExportService = new();
    private readonly BackupRepositoryService _backupService = new();
    private readonly ISettingsStore _settingsStore;
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly StorageConnectionService _storageConnectionService;
    private readonly ResumableExecutionService _resumableExecutionService;
    private readonly StorageTransferService _storageTransferService = new();
    private readonly SecureBackupArchiveService _secureArchiveService = new();
    private readonly VssBackupService _vssBackupService = new();
    private readonly TaskQueueService _taskQueueService;
    private readonly ITaskResultNotifier? _taskResultNotifier;
    private readonly NotificationChannelService? _notificationChannelService;
    private readonly PluginCatalog _pluginCatalog;
    private readonly WindowsCredentialStore? _credentialStore;
    private readonly NetworkHttpClientFactory? _networkClients;
    private Func<Task>? _autoTriggerReload;
    private string _sourceDirectory = string.Empty;
    private string _sourcePathsText = string.Empty;
    private string _targetDirectory = string.Empty;
    private string _taskName = "我的复制任务";
    private ModeOption _selectedMode;
    private VerificationOption _selectedVerification;
    private ExistingFilePolicyOption _selectedExistingFilePolicy;
    private ConflictResolutionOption _selectedConflictResolution;
    private ReparsePointOption _selectedReparsePoint;
    private string _includePatternsText = string.Empty;
    private string _excludePatternsText = string.Empty;
    private string _excludedDirectoriesText = string.Empty;
    private string _minimumSizeMbText = string.Empty;
    private string _maximumSizeMbText = string.Empty;
    private int _maxAttempts = 3;
    private int _maximumDeleteItems = 1000;
    private double _maximumDeletePercentage = 25;
    private CopyPlan? _currentPlan;
    private Guid? _currentTaskId;
    private DateTime? _currentTaskCreatedAtUtc;
    private SavedTaskViewModel? _selectedSavedTask;
    private RunHistoryViewModel? _selectedRun;
    private QuarantineEntryViewModel? _selectedQuarantineEntry;
    private SavedTaskViewModel? _selectedTaskForSchedule;
    private ScheduledTaskViewModel? _selectedSchedule;
    private ScheduleTriggerOption _selectedScheduleTrigger;
    private DayOfWeekOption _selectedScheduleDay;
    private string _scheduleTimeText = "02:00";
    private int _scheduleDayOfMonth = 1;
    private int _scheduleIntervalMinutes = 60;
    private bool _scheduleEnabled = true;
    private bool _scheduleRunMissed = true;
    private bool _scheduleAllowOverwrite;
    private bool _scheduleAllowQuarantine;
    private CancellationTokenSource? _operationCancellation;
    private string _statusMessage = "请选择源目录和目标目录，然后生成预览。";
    private bool _isBusy;
    private bool _canCancel;
    private double _progressPercent;
    private double _currentFileProgressPercent;
    private string _transferStatistics = string.Empty;
    private bool _includeHiddenFiles;
    private bool _includeSystemFiles;
    private string _modifiedAfterText = string.Empty;
    private string _modifiedBeforeText = string.Empty;
    private CopyExecutionControl? _executionControl;
    private CopyPlan? _lastExecutedPlan;
    private CopyExecutionResult? _lastExecutionResult;
    private DateTime _progressStartedAtUtc;
    private string _backupSourceDirectory = string.Empty;
    private string _backupRepositoryDirectory = string.Empty;
    private string _backupRestoreDirectory = string.Empty;
    private BackupModeOption _selectedBackupMode;
    private BackupSnapshotViewModel? _selectedBackupSnapshot;
    private int _backupKeepLatest = 10;
    private bool _backupRestoreOverwrite;
    private bool _notificationsEnabled = true;
    private bool _minimizeToTray = true;
    private bool _startWithWindows;
    private int _logRetentionDays = 180;
    private int _retryDelaySeconds = 1;
    private int _backgroundMaxParallelFiles = 1;
    private int _backgroundSpeedLimitMbPerSecond = 64;
    private int _bufferSizeKb = 1024;
    private int _speedLimitMbPerSecond;
    private int _maxParallelFiles = 4;
    private bool _autoTunePerformance = true;
    private bool _autoTriggerEnabled = true;
    private bool _autoTriggerFileChange = true;
    private int _autoTriggerDelaySeconds = 5;
    private bool _autoTriggerAllowOverwrite;
    private bool _autoTriggerAllowQuarantine;
    private AutoTriggerRuleViewModel? _selectedAutoTriggerRule;
    private StorageConnectionViewModel? _selectedStorageConnection;
    private StorageConnectionViewModel? _selectedTransferSource;
    private StorageConnectionViewModel? _selectedTransferTarget;
    private string _storageSourceDirectory = string.Empty;
    private string _storageTargetDirectory = string.Empty;
    private bool _storageTransferOverwrite;
    private string _storageTransferSummary = "请选择源连接和目标连接后预览。";
    private bool _backupUseVss;
    private IncompleteExecutionViewModel? _selectedIncompleteExecution;
    private string _orchestrationDescription = string.Empty;
    private string _orchestrationGroup = string.Empty;
    private string _orchestrationTags = string.Empty;
    private int _orchestrationPriority;
    private string _orchestrationDependencies = string.Empty;
    private string _beforeCommandPath = string.Empty;
    private string _beforeCommandArguments = string.Empty;
    private string _afterCommandPath = string.Empty;
    private string _afterCommandArguments = string.Empty;
    private bool _runAfterCommandOnFailure;
    private int _maximumParallelTasks = 1;
    private string _queueSummary = "勾选任务后运行队列。";
    private NotificationChannelViewModel? _selectedNotificationChannel;
    private PluginVerificationOption _selectedPluginVerification;
    private string _theme = "Light";
    private string _language = "zh-CN";
    private string _logLevel = "Information";
    private bool _useSystemProxy = true;
    private string _proxyUrl = string.Empty;
    private string _proxyUserName = string.Empty;
    private string _proxyPassword = string.Empty;
    private string? _selectedRecentPath;
    private string? _selectedFavoritePath;
    private ApplicationSettings _loadedSettings = new();
    private FilterRuleTemplateViewModel? _selectedFilterTemplate;
    private TaskTemplateViewModel? _selectedTaskTemplate;
    private string _newTemplateName = "新模板";
    private string _duplicateScanPaths = string.Empty;
    private DuplicateGroupViewModel? _selectedDuplicateGroup;
    private string _duplicateSummary = "添加一个或多个目录后扫描；重复检测不会删除任何文件。";
    private string _historyTaskFilter = string.Empty;
    private string _historyStatusFilter = string.Empty;
    private string _historyFromText = string.Empty;
    private string _historyToText = string.Empty;
    private string _selectedPlanFilter = "全部";
    private readonly ICollectionView _planItemsView;
    private DateTime _lastProgressSampleAtUtc;
    private long _lastProgressSampleBytes;
    private double _currentBytesPerSecond;
    private string _updateManifestUrl = string.Empty;
    private bool _checkUpdatesOnStartup;
    private string _updateStatus = "尚未检查更新。";
    private UpdateManifest? _availableUpdate;
    private readonly UpdateService _updateService;
    private bool _preserveOverwrittenFiles;
    private string _versionsDirectory = string.Empty;
    private int _versionKeepLatest = 10;
    private int _versionKeepDays = 30;
    private bool _preserveNtfsPermissions;
    private int _availabilityWaitMinutes = 2;
    private bool _useStableVolumePaths;
    private FileVersionViewModel? _selectedFileVersion;

    public MainViewModel(
        CopyWorkflow workflow,
        IFolderPicker folderPicker,
        ICopyToolsRepository repository,
        QuarantineWorkflow quarantineWorkflow,
        ScheduledTaskRunner scheduledTaskRunner,
        ISettingsStore settingsStore,
        StorageConnectionService storageConnectionService,
        ITaskResultNotifier? taskResultNotifier = null,
        NotificationChannelService? notificationChannelService = null,
        PluginCatalog? pluginCatalog = null,
        WindowsCredentialStore? credentialStore = null,
        NetworkHttpClientFactory? networkClients = null)
    {
        _workflow = workflow;
        _folderPicker = folderPicker;
        _repository = repository;
        _quarantineWorkflow = quarantineWorkflow;
        _scheduledTaskRunner = scheduledTaskRunner;
        _taskTransferService = new TaskTransferService(repository);
        _configurationBackupService = new ConfigurationBackupService(repository, settingsStore);
        _settingsStore = settingsStore;
        _storageConnectionService = storageConnectionService;
        _resumableExecutionService = new ResumableExecutionService(repository, workflow);
        _taskQueueService = new TaskQueueService(repository, scheduledTaskRunner);
        _taskResultNotifier = taskResultNotifier;
        _notificationChannelService = notificationChannelService;
        _pluginCatalog = pluginCatalog ?? new PluginCatalog([], new Dictionary<string, IPluginStorageAdapterFactory>(),
            new Dictionary<string, IFileVerificationProvider>(), []);
        _credentialStore = credentialStore;
        _networkClients = networkClients;
        Func<HttpClient>? updateClientFactory = networkClients is null ? null : () => networkClients.Create();
        _updateService = new UpdateService(updateClientFactory);
        _planItemsView = CollectionViewSource.GetDefaultView(PlanItems);
        _planItemsView.Filter = FilterPlanItem;

        Modes =
        [
            new ModeOption(CopyMode.FullCopy, "普通复制", "复制全部源文件；已有目标文件将被覆盖。"),
            new ModeOption(CopyMode.IncrementalCopy, "增量复制", "仅复制目标目录中不存在的文件。"),
            new ModeOption(CopyMode.OneWaySync, "单向同步", "传播源端新增、修改和已记录的删除；目标端额外文件保留。需先保存任务。"),
            new ModeOption(CopyMode.AccumulateSync, "累加同步", "复制新增和已变化文件，永不删除目标历史文件。"),
            new ModeOption(CopyMode.MirrorSync, "镜像同步", "让目标与源一致；目标多余文件会进入隔离区。"),
            new ModeOption(CopyMode.MoveSync, "移动同步", "复制并校验成功后，把源文件移入隔离区。"),
            new ModeOption(CopyMode.UpdateSync, "更新同步", "仅把源端本轮新增或变化的文件作为新批次同步到目标。需先保存任务。"),
            new ModeOption(CopyMode.TwoWaySync, "双向同步", "依据上次成功快照合并两端变化，并为并发修改保留冲突副本。需先保存任务。"),
            new ModeOption(CopyMode.VerifyOnly, "仅校验", "比较源端和目标端文件；不复制、不覆盖、不删除。"),
        ];
        _selectedMode = Modes[2];

        VerificationOptions =
        [
            new VerificationOption(VerificationMode.Quick, "快速校验", "比较复制前后的文件大小。"),
            new VerificationOption(VerificationMode.Sha256, "SHA-256 严格校验", "读取源和目标临时文件内容并比较哈希值。"),
            new VerificationOption(VerificationMode.SampledSha256, "采样 SHA-256 校验", "仅对文件首、中、尾采样计算哈希，兼顾速度与内容准确率。"),
        ];
        _selectedVerification = VerificationOptions[0];
        ConflictResolutionOptions =
        [
            new(ConflictResolutionPolicy.KeepBoth, "自动保留两份"),
            new(ConflictResolutionPolicy.SourceWins, "源端覆盖目标端"),
            new(ConflictResolutionPolicy.TargetWins, "目标端覆盖源端"),
            new(ConflictResolutionPolicy.Ask, "逐个冲突询问"),
        ];
        _selectedConflictResolution = ConflictResolutionOptions[0];
        ReparsePointOptions =
        [
            new(ReparsePointPolicy.Skip, "跳过符号链接和联接点（推荐）"),
            new(ReparsePointPolicy.FollowFileLinks, "跟随文件符号链接"),
            new(ReparsePointPolicy.FollowAll, "跟随文件和目录链接（检测循环）"),
        ];
        _selectedReparsePoint = ReparsePointOptions[0];
        PluginVerificationOptions =
        [
            new PluginVerificationOption(null, "不使用插件校验", "仅使用上方内置快速或 SHA-256 校验。"),
            .. _pluginCatalog.VerificationProviders.Values.Select(provider =>
                new PluginVerificationOption(provider.Id, provider.DisplayName, $"由插件 {provider.Id} 追加校验。")),
        ];
        _selectedPluginVerification = PluginVerificationOptions[0];
        ExistingFilePolicies =
        [
            new ExistingFilePolicyOption(ExistingFilePolicy.ModeDefault, "按同步模式决定"),
            new ExistingFilePolicyOption(ExistingFilePolicy.Overwrite, "覆盖"),
            new ExistingFilePolicyOption(ExistingFilePolicy.Skip, "跳过"),
            new ExistingFilePolicyOption(ExistingFilePolicy.Rename, "自动重命名"),
            new ExistingFilePolicyOption(ExistingFilePolicy.Ask, "执行前询问"),
        ];
        _selectedExistingFilePolicy = ExistingFilePolicies[0];
        BackupModes =
        [
            new BackupModeOption(BackupMode.IncrementalHistory, "历史增量备份", "内容相同的文件复用对象，可按任意成功时间点恢复。"),
            new BackupModeOption(BackupMode.FullSnapshot, "完全备份", "每次写入独立对象，快照之间互不依赖。"),
        ];
        _selectedBackupMode = BackupModes[0];

        ScheduleTriggers =
        [
            new ScheduleTriggerOption(ScheduleTrigger.Daily, "每天"),
            new ScheduleTriggerOption(ScheduleTrigger.Weekly, "每周"),
            new ScheduleTriggerOption(ScheduleTrigger.Monthly, "每月"),
            new ScheduleTriggerOption(ScheduleTrigger.Interval, "固定间隔"),
            new ScheduleTriggerOption(ScheduleTrigger.ApplicationStartup, "应用启动时"),
        ];
        _selectedScheduleTrigger = ScheduleTriggers[0];
        ScheduleDays =
        [
            new DayOfWeekOption(DayOfWeek.Monday, "星期一"),
            new DayOfWeekOption(DayOfWeek.Tuesday, "星期二"),
            new DayOfWeekOption(DayOfWeek.Wednesday, "星期三"),
            new DayOfWeekOption(DayOfWeek.Thursday, "星期四"),
            new DayOfWeekOption(DayOfWeek.Friday, "星期五"),
            new DayOfWeekOption(DayOfWeek.Saturday, "星期六"),
            new DayOfWeekOption(DayOfWeek.Sunday, "星期日"),
        ];
        _selectedScheduleDay = ScheduleDays[0];

        BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
        AddSourceFilesCommand = new RelayCommand(AddSourceFiles, () => !IsBusy);
        ClearSourcePathsCommand = new RelayCommand(ClearSourcePaths, () => !IsBusy);
        BrowseTargetCommand = new RelayCommand(BrowseTarget, () => !IsBusy);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, CanPreview, HandleUnexpectedException);
        ExecuteCommand = new AsyncRelayCommand(
            ExecuteAsync,
            () => !IsBusy && CurrentPlan is { } plan
                && (plan.OperationsToExecute > 0
                    || plan.Request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync),
            HandleUnexpectedException);
        SaveTaskCommand = new AsyncRelayCommand(SaveTaskAsync, CanSaveTask, HandleUnexpectedException);
        LoadTaskCommand = new RelayCommand(LoadSelectedTask, () => !IsBusy && SelectedSavedTask is not null);
        DeleteTaskCommand = new AsyncRelayCommand(
            DeleteSelectedTaskAsync,
            () => !IsBusy && SelectedSavedTask is not null,
            HandleUnexpectedException);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, () => !IsBusy, HandleUnexpectedException);
        RefreshQuarantineCommand = new AsyncRelayCommand(
            RefreshQuarantineAsync,
            () => !IsBusy,
            HandleUnexpectedException);
        RestoreQuarantineCommand = new AsyncRelayCommand(
            RestoreSelectedQuarantineAsync,
            () => !IsBusy && SelectedQuarantineEntry is not null,
            HandleUnexpectedException);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
        PauseCommand = new RelayCommand(Pause, () => IsBusy && _executionControl is { IsPaused: false });
        ResumeCommand = new RelayCommand(Resume, () => IsBusy && _executionControl is { IsPaused: true });
        RetryFailuresCommand = new AsyncRelayCommand(
            RetryFailuresAsync,
            () => !IsBusy && _lastExecutionResult is { IsSuccessful: false, WasCancelled: false }
                && _lastExecutedPlan is not null,
            HandleUnexpectedException);
        ExcludeSelectedPlanItemsCommand = new RelayCommand(
            ExcludeSelectedPlanItems,
            () => !IsBusy && CurrentPlan is not null && PlanItems.Any(item => item.IsSelected));
        SaveScheduleCommand = new AsyncRelayCommand(SaveScheduleAsync, CanSaveSchedule, HandleUnexpectedException);
        DeleteScheduleCommand = new AsyncRelayCommand(
            DeleteSelectedScheduleAsync,
            () => !IsBusy && SelectedSchedule is not null,
            HandleUnexpectedException);
        RunScheduleNowCommand = new AsyncRelayCommand(
            RunSelectedScheduleNowAsync,
            () => !IsBusy && SelectedSchedule is not null,
            HandleUnexpectedException);
        RefreshSchedulesCommand = new AsyncRelayCommand(RefreshSchedulesAsync, () => !IsBusy, HandleUnexpectedException);
        DuplicateTaskCommand = new AsyncRelayCommand(
            DuplicateSelectedTaskAsync,
            () => !IsBusy && SelectedSavedTask is not null,
            HandleUnexpectedException);
        ExportTasksCommand = new AsyncRelayCommand(ExportTasksAsync, () => !IsBusy, HandleUnexpectedException);
        ImportTasksCommand = new AsyncRelayCommand(ImportTasksAsync, () => !IsBusy, HandleUnexpectedException);
        ExportRunLogCommand = new AsyncRelayCommand(
            ExportSelectedRunLogAsync,
            () => !IsBusy && SelectedRun is not null,
            HandleUnexpectedException);
        BrowseBackupSourceCommand = new RelayCommand(BrowseBackupSource, () => !IsBusy);
        BrowseBackupRepositoryCommand = new RelayCommand(BrowseBackupRepository, () => !IsBusy);
        BrowseBackupRestoreCommand = new RelayCommand(BrowseBackupRestore, () => !IsBusy);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, CanCreateBackup, HandleUnexpectedException);
        RefreshBackupSnapshotsCommand = new AsyncRelayCommand(
            RefreshBackupSnapshotsAsync,
            () => !IsBusy && !string.IsNullOrWhiteSpace(BackupRepositoryDirectory),
            HandleUnexpectedException);
        RestoreBackupCommand = new AsyncRelayCommand(
            RestoreBackupAsync,
            () => !IsBusy && SelectedBackupSnapshot is not null
                && !string.IsNullOrWhiteSpace(BackupRestoreDirectory),
            HandleUnexpectedException);
        CleanupBackupsCommand = new AsyncRelayCommand(
            CleanupBackupsAsync,
            () => !IsBusy && !string.IsNullOrWhiteSpace(BackupRepositoryDirectory),
            HandleUnexpectedException);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy, HandleUnexpectedException);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync, () => !IsBusy, HandleUnexpectedException);
        ExportConfigurationCommand = new AsyncRelayCommand(ExportConfigurationAsync, () => !IsBusy, HandleUnexpectedException);
        RestoreConfigurationCommand = new AsyncRelayCommand(RestoreConfigurationAsync, () => !IsBusy, HandleUnexpectedException);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(UpdateManifestUrl), HandleUnexpectedException);
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, () => !IsBusy && _availableUpdate is not null, HandleUnexpectedException);
        BrowseVersionsDirectoryCommand = new RelayCommand(BrowseVersionsDirectory, () => !IsBusy);
        RefreshVersionsCommand = new RelayCommand(RefreshVersions, () => !IsBusy && !string.IsNullOrWhiteSpace(VersionsDirectory));
        RestoreVersionCommand = new RelayCommand(RestoreVersion, () => !IsBusy && SelectedFileVersion is not null);
        SaveAutoTriggerCommand = new AsyncRelayCommand(SaveAutoTriggerAsync, CanSaveSchedule, HandleUnexpectedException);
        DeleteAutoTriggerCommand = new AsyncRelayCommand(
            DeleteAutoTriggerAsync,
            () => !IsBusy && SelectedAutoTriggerRule is not null,
            HandleUnexpectedException);
        AddStorageConnectionCommand = new RelayCommand(AddStorageConnection, () => !IsBusy);
        EditStorageConnectionCommand = new RelayCommand(
            EditStorageConnection,
            () => !IsBusy && SelectedStorageConnection is not null);
        DeleteStorageConnectionCommand = new AsyncRelayCommand(
            DeleteStorageConnectionAsync,
            () => !IsBusy && SelectedStorageConnection is not null,
            HandleUnexpectedException);
        TestStorageConnectionCommand = new AsyncRelayCommand(
            TestStorageConnectionAsync,
            () => !IsBusy && SelectedStorageConnection is not null,
            HandleUnexpectedException);
        PreviewStorageTransferCommand = new AsyncRelayCommand(
            PreviewStorageTransferAsync,
            CanTransferStorage,
            HandleUnexpectedException);
        ExecuteStorageTransferCommand = new AsyncRelayCommand(
            ExecuteStorageTransferAsync,
            CanTransferStorage,
            HandleUnexpectedException);
        CreateSecureArchiveCommand = new AsyncRelayCommand(
            CreateSecureArchiveAsync,
            () => !IsBusy,
            HandleUnexpectedException);
        RestoreSecureArchiveCommand = new AsyncRelayCommand(
            RestoreSecureArchiveAsync,
            () => !IsBusy,
            HandleUnexpectedException);
        RefreshIncompleteCommand = new AsyncRelayCommand(
            RefreshIncompleteAsync,
            () => !IsBusy,
            HandleUnexpectedException);
        ResumeIncompleteCommand = new AsyncRelayCommand(
            ResumeIncompleteAsync,
            () => !IsBusy && SelectedIncompleteExecution is not null,
            HandleUnexpectedException);
        SaveOrchestrationCommand = new AsyncRelayCommand(
            SaveOrchestrationAsync,
            () => !IsBusy && SelectedSavedTask is not null,
            HandleUnexpectedException);
        RunTaskQueueCommand = new AsyncRelayCommand(
            RunTaskQueueAsync,
            () => !IsBusy,
            HandleUnexpectedException);
        AddNotificationChannelCommand = new RelayCommand(AddNotificationChannel, () => !IsBusy);
        EditNotificationChannelCommand = new RelayCommand(
            EditNotificationChannel, () => !IsBusy && SelectedNotificationChannel is not null);
        DeleteNotificationChannelCommand = new AsyncRelayCommand(
            DeleteNotificationChannelAsync, () => !IsBusy && SelectedNotificationChannel is not null,
            HandleUnexpectedException);
        TestNotificationChannelCommand = new AsyncRelayCommand(
            TestNotificationChannelAsync, () => !IsBusy && SelectedNotificationChannel is not null,
            HandleUnexpectedException);
        AddCurrentPathsToFavoritesCommand = new RelayCommand(AddCurrentPathsToFavorites, () => !IsBusy);
        UseRecentAsSourceCommand = new RelayCommand(() => UseQuickPath(SelectedRecentPath, true), () => !IsBusy && SelectedRecentPath is not null);
        UseFavoriteAsSourceCommand = new RelayCommand(() => UseQuickPath(SelectedFavoritePath, true), () => !IsBusy && SelectedFavoritePath is not null);
        UseFavoriteAsTargetCommand = new RelayCommand(() => UseQuickPath(SelectedFavoritePath, false), () => !IsBusy && SelectedFavoritePath is not null);
        RemoveFavoriteCommand = new RelayCommand(RemoveFavorite, () => !IsBusy && SelectedFavoritePath is not null);
        SaveFilterTemplateCommand = new AsyncRelayCommand(SaveFilterTemplateAsync, () => !IsBusy, HandleUnexpectedException);
        ApplyFilterTemplateCommand = new RelayCommand(ApplyFilterTemplate, () => !IsBusy && SelectedFilterTemplate is not null);
        DeleteFilterTemplateCommand = new AsyncRelayCommand(DeleteFilterTemplateAsync, () => !IsBusy && SelectedFilterTemplate is not null, HandleUnexpectedException);
        SaveTaskTemplateCommand = new AsyncRelayCommand(SaveTaskTemplateAsync, () => !IsBusy, HandleUnexpectedException);
        ApplyTaskTemplateCommand = new RelayCommand(ApplyTaskTemplate, () => !IsBusy && SelectedTaskTemplate is not null);
        DeleteTaskTemplateCommand = new AsyncRelayCommand(DeleteTaskTemplateAsync, () => !IsBusy && SelectedTaskTemplate is not null, HandleUnexpectedException);
        BrowseDuplicateDirectoryCommand = new RelayCommand(BrowseDuplicateDirectory, () => !IsBusy);
        ScanDuplicatesCommand = new AsyncRelayCommand(ScanDuplicatesAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(DuplicateScanPaths), HandleUnexpectedException);
        QueryHistoryCommand = new AsyncRelayCommand(QueryHistoryAsync, () => !IsBusy, HandleUnexpectedException);
        RerunSelectedHistoryCommand = new RelayCommand(RerunSelectedHistory, () => !IsBusy && SelectedRun is not null);
        OpenDataDirectoryCommand = new RelayCommand(OpenDataDirectory, () => !IsBusy);
        NewCopyTaskCommand = new RelayCommand(() => StartNewTask(CopyMode.FullCopy), () => !IsBusy);
        NewSyncTaskCommand = new RelayCommand(() => StartNewTask(CopyMode.OneWaySync), () => !IsBusy);
    }

    public IReadOnlyList<ModeOption> Modes { get; }

    public IReadOnlyList<VerificationOption> VerificationOptions { get; }

    public IReadOnlyList<PluginVerificationOption> PluginVerificationOptions { get; }

    public ObservableCollection<PluginViewModel> Plugins { get; } = [];
    public ObservableCollection<PluginDiagnosticViewModel> PluginDiagnostics { get; } = [];

    public string PluginDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyTools", "plugins");

    public PluginVerificationOption SelectedPluginVerification
    {
        get => _selectedPluginVerification;
        set
        {
            if (SetProperty(ref _selectedPluginVerification, value)) InvalidatePlan();
        }
    }

    public IReadOnlyList<ExistingFilePolicyOption> ExistingFilePolicies { get; }
    public IReadOnlyList<ConflictResolutionOption> ConflictResolutionOptions { get; }
    public IReadOnlyList<ReparsePointOption> ReparsePointOptions { get; }

    public ConflictResolutionOption SelectedConflictResolution
    {
        get => _selectedConflictResolution;
        set { if (SetProperty(ref _selectedConflictResolution, value)) InvalidatePlan(); }
    }

    public ReparsePointOption SelectedReparsePoint
    {
        get => _selectedReparsePoint;
        set { if (SetProperty(ref _selectedReparsePoint, value)) InvalidatePlan(); }
    }

    public IReadOnlyList<BackupModeOption> BackupModes { get; }

    public IReadOnlyList<ScheduleTriggerOption> ScheduleTriggers { get; }

    public IReadOnlyList<DayOfWeekOption> ScheduleDays { get; }

    public IReadOnlyList<string> Themes { get; } = ["Light", "Dark", "System"];
    public IReadOnlyList<string> Languages { get; } = ["zh-CN", "en-US", "System"];
    public IReadOnlyList<string> LogLevels { get; } = ["Debug", "Information", "Warning", "Error"];
    public ObservableCollection<string> RecentPaths { get; } = [];
    public ObservableCollection<string> FavoritePaths { get; } = [];
    public ObservableCollection<FilterRuleTemplateViewModel> FilterTemplates { get; } = [];
    public ObservableCollection<TaskTemplateViewModel> TaskTemplates { get; } = [];
    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = [];

    public string Theme { get => _theme; set => SetProperty(ref _theme, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }
    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }
    public bool UseSystemProxy { get => _useSystemProxy; set => SetProperty(ref _useSystemProxy, value); }
    public string ProxyUrl { get => _proxyUrl; set => SetProperty(ref _proxyUrl, value); }
    public string ProxyUserName { get => _proxyUserName; set => SetProperty(ref _proxyUserName, value); }
    public string ProxyPassword { get => _proxyPassword; set => SetProperty(ref _proxyPassword, value); }
    public string UpdateManifestUrl { get => _updateManifestUrl; set { if (SetProperty(ref _updateManifestUrl, value)) CheckUpdateCommand.NotifyCanExecuteChanged(); } }
    public bool CheckUpdatesOnStartup { get => _checkUpdatesOnStartup; set => SetProperty(ref _checkUpdatesOnStartup, value); }
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }
    public bool PreserveOverwrittenFiles { get => _preserveOverwrittenFiles; set { if (SetProperty(ref _preserveOverwrittenFiles, value)) InvalidatePlan(); } }
    public string VersionsDirectory { get => _versionsDirectory; set { if (SetProperty(ref _versionsDirectory, value)) { InvalidatePlan(); RefreshVersionsCommand.NotifyCanExecuteChanged(); } } }
    public int VersionKeepLatest { get => _versionKeepLatest; set { if (SetProperty(ref _versionKeepLatest, Math.Clamp(value, 1, 10000))) InvalidatePlan(); } }
    public int VersionKeepDays { get => _versionKeepDays; set { if (SetProperty(ref _versionKeepDays, Math.Clamp(value, 1, 36500))) InvalidatePlan(); } }
    public bool PreserveNtfsPermissions { get => _preserveNtfsPermissions; set { if (SetProperty(ref _preserveNtfsPermissions, value)) InvalidatePlan(); } }
    public int AvailabilityWaitMinutes { get => _availabilityWaitMinutes; set { if (SetProperty(ref _availabilityWaitMinutes, Math.Clamp(value, 0, 1440))) InvalidatePlan(); } }
    public bool UseStableVolumePaths { get => _useStableVolumePaths; set { if (SetProperty(ref _useStableVolumePaths, value)) InvalidatePlan(); } }
    public ObservableCollection<FileVersionViewModel> FileVersions { get; } = [];
    public FileVersionViewModel? SelectedFileVersion { get => _selectedFileVersion; set { if (SetProperty(ref _selectedFileVersion, value)) RestoreVersionCommand.NotifyCanExecuteChanged(); } }
    public string? SelectedRecentPath { get => _selectedRecentPath; set => SetProperty(ref _selectedRecentPath, value); }
    public string? SelectedFavoritePath { get => _selectedFavoritePath; set => SetProperty(ref _selectedFavoritePath, value); }
    public FilterRuleTemplateViewModel? SelectedFilterTemplate { get => _selectedFilterTemplate; set => SetProperty(ref _selectedFilterTemplate, value); }
    public TaskTemplateViewModel? SelectedTaskTemplate { get => _selectedTaskTemplate; set => SetProperty(ref _selectedTaskTemplate, value); }
    public string NewTemplateName { get => _newTemplateName; set => SetProperty(ref _newTemplateName, value); }
    public string DuplicateScanPaths { get => _duplicateScanPaths; set => SetProperty(ref _duplicateScanPaths, value); }
    public DuplicateGroupViewModel? SelectedDuplicateGroup { get => _selectedDuplicateGroup; set => SetProperty(ref _selectedDuplicateGroup, value); }
    public string DuplicateSummary { get => _duplicateSummary; private set => SetProperty(ref _duplicateSummary, value); }
    public string HistoryTaskFilter { get => _historyTaskFilter; set => SetProperty(ref _historyTaskFilter, value); }
    public string HistoryStatusFilter { get => _historyStatusFilter; set => SetProperty(ref _historyStatusFilter, value); }
    public string HistoryFromText { get => _historyFromText; set => SetProperty(ref _historyFromText, value); }
    public string HistoryToText { get => _historyToText; set => SetProperty(ref _historyToText, value); }

    public ObservableCollection<PlanItemViewModel> PlanItems { get; } = [];
    public ICollectionView PlanItemsView => _planItemsView;
    public IReadOnlyList<string> PlanFilters { get; } = ["全部", "新增", "覆盖", "跳过", "删除/隔离", "冲突", "校验", "目录"];
    public string SelectedPlanFilter
    {
        get => _selectedPlanFilter;
        set { if (SetProperty(ref _selectedPlanFilter, value)) _planItemsView.Refresh(); }
    }

    public ObservableCollection<SavedTaskViewModel> SavedTasks { get; } = [];
    public event EventHandler? SavedTasksChanged;
    public event EventHandler<TaskResultNotification>? TaskFinished;

    public ObservableCollection<RunHistoryViewModel> RunHistory { get; } = [];

    public ObservableCollection<QuarantineEntryViewModel> QuarantineEntries { get; } = [];

    public ObservableCollection<ScheduledTaskViewModel> Schedules { get; } = [];

    public ObservableCollection<BackupSnapshotViewModel> BackupSnapshots { get; } = [];

    public ObservableCollection<BackupFileViewModel> BackupFiles { get; } = [];

    public ObservableCollection<AutoTriggerRuleViewModel> AutoTriggerRules { get; } = [];

    public ObservableCollection<StorageConnectionViewModel> StorageConnections { get; } = [];

    public ObservableCollection<IncompleteExecutionViewModel> IncompleteExecutions { get; } = [];
    public ObservableCollection<NotificationChannelViewModel> NotificationChannels { get; } = [];

    public NotificationChannelViewModel? SelectedNotificationChannel
    {
        get => _selectedNotificationChannel;
        set
        {
            if (SetProperty(ref _selectedNotificationChannel, value))
            {
                EditNotificationChannelCommand.NotifyCanExecuteChanged();
                DeleteNotificationChannelCommand.NotifyCanExecuteChanged();
                TestNotificationChannelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StorageConnectionViewModel? SelectedStorageConnection
    {
        get => _selectedStorageConnection;
        set
        {
            if (SetProperty(ref _selectedStorageConnection, value))
            {
                EditStorageConnectionCommand.NotifyCanExecuteChanged();
                DeleteStorageConnectionCommand.NotifyCanExecuteChanged();
                TestStorageConnectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public StorageConnectionViewModel? SelectedTransferSource
    {
        get => _selectedTransferSource;
        set
        {
            if (SetProperty(ref _selectedTransferSource, value))
            {
                RefreshStorageTransferCommands();
            }
        }
    }

    public StorageConnectionViewModel? SelectedTransferTarget
    {
        get => _selectedTransferTarget;
        set
        {
            if (SetProperty(ref _selectedTransferTarget, value))
            {
                RefreshStorageTransferCommands();
            }
        }
    }

    public string StorageSourceDirectory
    {
        get => _storageSourceDirectory;
        set
        {
            if (SetProperty(ref _storageSourceDirectory, value)) RefreshStorageTransferCommands();
        }
    }

    public string StorageTargetDirectory
    {
        get => _storageTargetDirectory;
        set
        {
            if (SetProperty(ref _storageTargetDirectory, value)) RefreshStorageTransferCommands();
        }
    }

    public bool StorageTransferOverwrite
    {
        get => _storageTransferOverwrite;
        set => SetProperty(ref _storageTransferOverwrite, value);
    }

    public string StorageTransferSummary
    {
        get => _storageTransferSummary;
        private set => SetProperty(ref _storageTransferSummary, value);
    }

    public bool BackupUseVss
    {
        get => _backupUseVss;
        set => SetProperty(ref _backupUseVss, value);
    }

    public IncompleteExecutionViewModel? SelectedIncompleteExecution
    {
        get => _selectedIncompleteExecution;
        set
        {
            if (SetProperty(ref _selectedIncompleteExecution, value))
                ResumeIncompleteCommand.NotifyCanExecuteChanged();
        }
    }

    public string TaskName
    {
        get => _taskName;
        set
        {
            if (SetProperty(ref _taskName, value))
            {
                SaveTaskCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SourceDirectory
    {
        get => _sourceDirectory;
        set
        {
            if (SetProperty(ref _sourceDirectory, value))
            {
                _sourcePathsText = value;
                OnPropertyChanged(nameof(SourcePathsText));
                InvalidatePlan();
            }
        }
    }

    public string SourcePathsText
    {
        get => _sourcePathsText;
        set
        {
            if (SetProperty(ref _sourcePathsText, value))
            {
                var first = ParseSourcePaths(value).FirstOrDefault() ?? string.Empty;
                if (!string.Equals(_sourceDirectory, first, StringComparison.Ordinal))
                {
                    _sourceDirectory = first;
                    OnPropertyChanged(nameof(SourceDirectory));
                }
                InvalidatePlan();
            }
        }
    }

    public string TargetDirectory
    {
        get => _targetDirectory;
        set
        {
            if (SetProperty(ref _targetDirectory, value))
            {
                InvalidatePlan();
            }
        }
    }

    public ModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(ModeDescription));
                InvalidatePlan();
            }
        }
    }

    public VerificationOption SelectedVerification
    {
        get => _selectedVerification;
        set
        {
            if (SetProperty(ref _selectedVerification, value))
            {
                OnPropertyChanged(nameof(VerificationDescription));
                InvalidatePlan();
            }
        }
    }

    public ExistingFilePolicyOption SelectedExistingFilePolicy
    {
        get => _selectedExistingFilePolicy;
        set
        {
            if (SetProperty(ref _selectedExistingFilePolicy, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string IncludePatternsText
    {
        get => _includePatternsText;
        set
        {
            if (SetProperty(ref _includePatternsText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string ExcludePatternsText
    {
        get => _excludePatternsText;
        set
        {
            if (SetProperty(ref _excludePatternsText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string ExcludedDirectoriesText
    {
        get => _excludedDirectoriesText;
        set
        {
            if (SetProperty(ref _excludedDirectoriesText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string MinimumSizeMbText
    {
        get => _minimumSizeMbText;
        set
        {
            if (SetProperty(ref _minimumSizeMbText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string MaximumSizeMbText
    {
        get => _maximumSizeMbText;
        set
        {
            if (SetProperty(ref _maximumSizeMbText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public int MaxAttempts
    {
        get => _maxAttempts;
        set
        {
            if (SetProperty(ref _maxAttempts, Math.Clamp(value, 1, 10)))
            {
                InvalidatePlan();
            }
        }
    }

    public int MaximumDeleteItems
    {
        get => _maximumDeleteItems;
        set
        {
            if (SetProperty(ref _maximumDeleteItems, Math.Clamp(value, 1, 1_000_000)))
            {
                InvalidatePlan();
            }
        }
    }

    public double MaximumDeletePercentage
    {
        get => _maximumDeletePercentage;
        set
        {
            if (SetProperty(ref _maximumDeletePercentage, Math.Clamp(value, 0.1, 100)))
            {
                InvalidatePlan();
            }
        }
    }

    public string ModeDescription => SelectedMode.Description;

    public string VerificationDescription => SelectedVerification.Description;

    public SavedTaskViewModel? SelectedSavedTask
    {
        get => _selectedSavedTask;
        set
        {
            if (SetProperty(ref _selectedSavedTask, value))
            {
                LoadTaskCommand.NotifyCanExecuteChanged();
                DeleteTaskCommand.NotifyCanExecuteChanged();
                DuplicateTaskCommand.NotifyCanExecuteChanged();
                SaveOrchestrationCommand.NotifyCanExecuteChanged();
                LoadOrchestration(value?.ExecutionOptions);
            }
        }
    }

    public string OrchestrationDescription { get => _orchestrationDescription; set => SetProperty(ref _orchestrationDescription, value); }
    public string OrchestrationGroup { get => _orchestrationGroup; set => SetProperty(ref _orchestrationGroup, value); }
    public string OrchestrationTags { get => _orchestrationTags; set => SetProperty(ref _orchestrationTags, value); }
    public int OrchestrationPriority { get => _orchestrationPriority; set => SetProperty(ref _orchestrationPriority, Math.Clamp(value, -100, 100)); }
    public string OrchestrationDependencies { get => _orchestrationDependencies; set => SetProperty(ref _orchestrationDependencies, value); }
    public string BeforeCommandPath { get => _beforeCommandPath; set => SetProperty(ref _beforeCommandPath, value); }
    public string BeforeCommandArguments { get => _beforeCommandArguments; set => SetProperty(ref _beforeCommandArguments, value); }
    public string AfterCommandPath { get => _afterCommandPath; set => SetProperty(ref _afterCommandPath, value); }
    public string AfterCommandArguments { get => _afterCommandArguments; set => SetProperty(ref _afterCommandArguments, value); }
    public bool RunAfterCommandOnFailure { get => _runAfterCommandOnFailure; set => SetProperty(ref _runAfterCommandOnFailure, value); }
    public int MaximumParallelTasks { get => _maximumParallelTasks; set => SetProperty(ref _maximumParallelTasks, Math.Clamp(value, 1, 16)); }
    public string QueueSummary { get => _queueSummary; private set => SetProperty(ref _queueSummary, value); }

    public RunHistoryViewModel? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                ExportRunLogCommand.NotifyCanExecuteChanged();
                RerunSelectedHistoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public QuarantineEntryViewModel? SelectedQuarantineEntry
    {
        get => _selectedQuarantineEntry;
        set
        {
            if (SetProperty(ref _selectedQuarantineEntry, value))
            {
                RestoreQuarantineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public SavedTaskViewModel? SelectedTaskForSchedule
    {
        get => _selectedTaskForSchedule;
        set
        {
            if (SetProperty(ref _selectedTaskForSchedule, value))
            {
                SaveScheduleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ScheduledTaskViewModel? SelectedSchedule
    {
        get => _selectedSchedule;
        set
        {
            if (SetProperty(ref _selectedSchedule, value))
            {
                DeleteScheduleCommand.NotifyCanExecuteChanged();
                RunScheduleNowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ScheduleTriggerOption SelectedScheduleTrigger
    {
        get => _selectedScheduleTrigger;
        set => SetProperty(ref _selectedScheduleTrigger, value);
    }

    public DayOfWeekOption SelectedScheduleDay
    {
        get => _selectedScheduleDay;
        set => SetProperty(ref _selectedScheduleDay, value);
    }

    public string ScheduleTimeText
    {
        get => _scheduleTimeText;
        set => SetProperty(ref _scheduleTimeText, value);
    }

    public int ScheduleDayOfMonth
    {
        get => _scheduleDayOfMonth;
        set => SetProperty(ref _scheduleDayOfMonth, Math.Clamp(value, 1, 31));
    }

    public int ScheduleIntervalMinutes
    {
        get => _scheduleIntervalMinutes;
        set => SetProperty(ref _scheduleIntervalMinutes, Math.Clamp(value, 1, 525_600));
    }

    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set => SetProperty(ref _scheduleEnabled, value);
    }

    public bool ScheduleRunMissed
    {
        get => _scheduleRunMissed;
        set => SetProperty(ref _scheduleRunMissed, value);
    }

    public bool ScheduleAllowOverwrite
    {
        get => _scheduleAllowOverwrite;
        set => SetProperty(ref _scheduleAllowOverwrite, value);
    }

    public bool ScheduleAllowQuarantine
    {
        get => _scheduleAllowQuarantine;
        set => SetProperty(ref _scheduleAllowQuarantine, value);
    }

    public string BackupSourceDirectory
    {
        get => _backupSourceDirectory;
        set
        {
            if (SetProperty(ref _backupSourceDirectory, value))
            {
                CreateBackupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BackupRepositoryDirectory
    {
        get => _backupRepositoryDirectory;
        set
        {
            if (SetProperty(ref _backupRepositoryDirectory, value))
            {
                CreateBackupCommand.NotifyCanExecuteChanged();
                RefreshBackupSnapshotsCommand.NotifyCanExecuteChanged();
                CleanupBackupsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BackupRestoreDirectory
    {
        get => _backupRestoreDirectory;
        set
        {
            if (SetProperty(ref _backupRestoreDirectory, value))
            {
                RestoreBackupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public BackupModeOption SelectedBackupMode
    {
        get => _selectedBackupMode;
        set => SetProperty(ref _selectedBackupMode, value);
    }

    public BackupSnapshotViewModel? SelectedBackupSnapshot
    {
        get => _selectedBackupSnapshot;
        set
        {
            if (SetProperty(ref _selectedBackupSnapshot, value))
            {
                BackupFiles.Clear();
                if (value is not null)
                {
                    foreach (var file in value.Snapshot.Files)
                    {
                        BackupFiles.Add(new BackupFileViewModel(file));
                    }
                }

                RestoreBackupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int BackupKeepLatest
    {
        get => _backupKeepLatest;
        set => SetProperty(ref _backupKeepLatest, Math.Clamp(value, 1, 10_000));
    }

    public bool BackupRestoreOverwrite
    {
        get => _backupRestoreOverwrite;
        set => SetProperty(ref _backupRestoreOverwrite, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => SetProperty(ref _logRetentionDays, Math.Clamp(value, 1, 3650));
    }

    public int RetryDelaySeconds
    {
        get => _retryDelaySeconds;
        set { if (SetProperty(ref _retryDelaySeconds, Math.Clamp(value, 0, 3600))) InvalidatePlan(); }
    }

    public int BackgroundMaxParallelFiles
    {
        get => _backgroundMaxParallelFiles;
        set => SetProperty(ref _backgroundMaxParallelFiles, Math.Clamp(value, 1, 16));
    }

    public int BackgroundSpeedLimitMbPerSecond
    {
        get => _backgroundSpeedLimitMbPerSecond;
        set => SetProperty(ref _backgroundSpeedLimitMbPerSecond, Math.Clamp(value, 0, 10 * 1024));
    }

    public int BufferSizeKb
    {
        get => _bufferSizeKb;
        set
        {
            if (SetProperty(ref _bufferSizeKb, Math.Clamp(value, 64, 16 * 1024)))
            {
                InvalidatePlan();
            }
        }
    }

    public int SpeedLimitMbPerSecond
    {
        get => _speedLimitMbPerSecond;
        set
        {
            if (SetProperty(ref _speedLimitMbPerSecond, Math.Clamp(value, 0, 10 * 1024)))
            {
                InvalidatePlan();
            }
        }
    }

    public int MaxParallelFiles
    {
        get => _maxParallelFiles;
        set
        {
            if (SetProperty(ref _maxParallelFiles, Math.Clamp(value, 1, 16)))
            {
                InvalidatePlan();
            }
        }
    }

    public bool AutoTunePerformance
    {
        get => _autoTunePerformance;
        set { if (SetProperty(ref _autoTunePerformance, value)) InvalidatePlan(); }
    }

    public bool AutoTriggerEnabled { get => _autoTriggerEnabled; set => SetProperty(ref _autoTriggerEnabled, value); }

    public bool AutoTriggerFileChange { get => _autoTriggerFileChange; set => SetProperty(ref _autoTriggerFileChange, value); }

    public int AutoTriggerDelaySeconds
    {
        get => _autoTriggerDelaySeconds;
        set => SetProperty(ref _autoTriggerDelaySeconds, Math.Clamp(value, 1, 3600));
    }

    public bool AutoTriggerAllowOverwrite { get => _autoTriggerAllowOverwrite; set => SetProperty(ref _autoTriggerAllowOverwrite, value); }

    public bool AutoTriggerAllowQuarantine { get => _autoTriggerAllowQuarantine; set => SetProperty(ref _autoTriggerAllowQuarantine, value); }

    public AutoTriggerRuleViewModel? SelectedAutoTriggerRule
    {
        get => _selectedAutoTriggerRule;
        set
        {
            if (SetProperty(ref _selectedAutoTriggerRule, value))
            {
                DeleteAutoTriggerCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public CopyPlan? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (SetProperty(ref _currentPlan, value))
            {
                OnPropertyChanged(nameof(HasPlan));
                OnPropertyChanged(nameof(PlanSummary));
                OnPropertyChanged(nameof(DestructivePlanSummary));
                ExecuteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasPlan => CurrentPlan is not null;

    public string PlanSummary => CurrentPlan is null
        ? "尚未生成操作计划"
        : $"扫描 {CurrentPlan.TotalFiles:N0} 个文件；将复制 {CurrentPlan.FilesToCopy:N0} 个，"
          + $"其中新增 {CurrentPlan.NewFiles:N0} 个、覆盖 {CurrentPlan.FilesToOverwrite:N0} 个；"
          + $"重命名 {CurrentPlan.FilesToRename:N0} 个；"
          + $"冲突 {CurrentPlan.Conflicts:N0} 组；跳过 {CurrentPlan.SkippedFiles:N0} 个；"
          + $"创建空目录 {CurrentPlan.DirectoriesToCreate:N0} 个；数据量 {FormatBytes(CurrentPlan.BytesToCopy)}。";

    public string DestructivePlanSummary => CurrentPlan is null
        ? string.Empty
        : $"隔离目标 {CurrentPlan.TargetFilesToQuarantine:N0} 个；"
          + $"隔离源端 {CurrentPlan.SourceFilesToQuarantine:N0} 个；"
          + $"移动清理源文件 {CurrentPlan.SourceFilesToRemove:N0} 个。";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            if (SetProperty(ref _canCancel, value))
            {
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public double CurrentFileProgressPercent
    {
        get => _currentFileProgressPercent;
        private set => SetProperty(ref _currentFileProgressPercent, value);
    }

    public string TransferStatistics
    {
        get => _transferStatistics;
        private set => SetProperty(ref _transferStatistics, value);
    }

    public bool IncludeHiddenFiles
    {
        get => _includeHiddenFiles;
        set
        {
            if (SetProperty(ref _includeHiddenFiles, value))
            {
                InvalidatePlan();
            }
        }
    }

    public bool IncludeSystemFiles
    {
        get => _includeSystemFiles;
        set
        {
            if (SetProperty(ref _includeSystemFiles, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string ModifiedAfterText
    {
        get => _modifiedAfterText;
        set
        {
            if (SetProperty(ref _modifiedAfterText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public string ModifiedBeforeText
    {
        get => _modifiedBeforeText;
        set
        {
            if (SetProperty(ref _modifiedBeforeText, value))
            {
                InvalidatePlan();
            }
        }
    }

    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand AddSourceFilesCommand { get; }
    public RelayCommand ClearSourcePathsCommand { get; }

    public RelayCommand BrowseTargetCommand { get; }

    public AsyncRelayCommand PreviewCommand { get; }

    public AsyncRelayCommand ExecuteCommand { get; }

    public AsyncRelayCommand SaveTaskCommand { get; }

    public RelayCommand LoadTaskCommand { get; }

    public AsyncRelayCommand DeleteTaskCommand { get; }

    public AsyncRelayCommand RefreshHistoryCommand { get; }

    public AsyncRelayCommand RefreshQuarantineCommand { get; }

    public AsyncRelayCommand RestoreQuarantineCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand ResumeCommand { get; }

    public AsyncRelayCommand RetryFailuresCommand { get; }

    public RelayCommand ExcludeSelectedPlanItemsCommand { get; }

    public AsyncRelayCommand SaveScheduleCommand { get; }

    public AsyncRelayCommand DeleteScheduleCommand { get; }

    public AsyncRelayCommand RunScheduleNowCommand { get; }

    public AsyncRelayCommand RefreshSchedulesCommand { get; }

    public AsyncRelayCommand DuplicateTaskCommand { get; }

    public AsyncRelayCommand ExportTasksCommand { get; }

    public AsyncRelayCommand ImportTasksCommand { get; }

    public AsyncRelayCommand ExportRunLogCommand { get; }

    public RelayCommand BrowseBackupSourceCommand { get; }

    public RelayCommand BrowseBackupRepositoryCommand { get; }

    public RelayCommand BrowseBackupRestoreCommand { get; }

    public AsyncRelayCommand CreateBackupCommand { get; }

    public AsyncRelayCommand RefreshBackupSnapshotsCommand { get; }

    public AsyncRelayCommand RestoreBackupCommand { get; }

    public AsyncRelayCommand CleanupBackupsCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand ResetSettingsCommand { get; }

    public AsyncRelayCommand ExportConfigurationCommand { get; }

    public AsyncRelayCommand RestoreConfigurationCommand { get; }
    public AsyncRelayCommand CheckUpdateCommand { get; }
    public AsyncRelayCommand DownloadUpdateCommand { get; }
    public RelayCommand BrowseVersionsDirectoryCommand { get; }
    public RelayCommand RefreshVersionsCommand { get; }
    public RelayCommand RestoreVersionCommand { get; }

    public AsyncRelayCommand SaveAutoTriggerCommand { get; }

    public AsyncRelayCommand DeleteAutoTriggerCommand { get; }

    public RelayCommand AddStorageConnectionCommand { get; }

    public RelayCommand EditStorageConnectionCommand { get; }

    public AsyncRelayCommand DeleteStorageConnectionCommand { get; }

    public AsyncRelayCommand TestStorageConnectionCommand { get; }

    public AsyncRelayCommand PreviewStorageTransferCommand { get; }

    public AsyncRelayCommand ExecuteStorageTransferCommand { get; }

    public AsyncRelayCommand CreateSecureArchiveCommand { get; }

    public AsyncRelayCommand RestoreSecureArchiveCommand { get; }

    public AsyncRelayCommand RefreshIncompleteCommand { get; }

    public AsyncRelayCommand ResumeIncompleteCommand { get; }

    public AsyncRelayCommand SaveOrchestrationCommand { get; }

    public AsyncRelayCommand RunTaskQueueCommand { get; }

    public RelayCommand AddNotificationChannelCommand { get; }
    public RelayCommand EditNotificationChannelCommand { get; }
    public AsyncRelayCommand DeleteNotificationChannelCommand { get; }
    public AsyncRelayCommand TestNotificationChannelCommand { get; }
    public RelayCommand AddCurrentPathsToFavoritesCommand { get; }
    public RelayCommand UseRecentAsSourceCommand { get; }
    public RelayCommand UseFavoriteAsSourceCommand { get; }
    public RelayCommand UseFavoriteAsTargetCommand { get; }
    public RelayCommand RemoveFavoriteCommand { get; }
    public AsyncRelayCommand SaveFilterTemplateCommand { get; }
    public RelayCommand ApplyFilterTemplateCommand { get; }
    public AsyncRelayCommand DeleteFilterTemplateCommand { get; }
    public AsyncRelayCommand SaveTaskTemplateCommand { get; }
    public RelayCommand ApplyTaskTemplateCommand { get; }
    public AsyncRelayCommand DeleteTaskTemplateCommand { get; }
    public RelayCommand BrowseDuplicateDirectoryCommand { get; }
    public AsyncRelayCommand ScanDuplicatesCommand { get; }
    public AsyncRelayCommand QueryHistoryCommand { get; }
    public RelayCommand RerunSelectedHistoryCommand { get; }
    public RelayCommand OpenDataDirectoryCommand { get; }
    public RelayCommand NewCopyTaskCommand { get; }
    public RelayCommand NewSyncTaskCommand { get; }

    public async Task InitializeAsync()
    {
        foreach (var plugin in _pluginCatalog.Plugins) Plugins.Add(new PluginViewModel(plugin));
        foreach (var diagnostic in _pluginCatalog.Diagnostics) PluginDiagnostics.Add(new PluginDiagnosticViewModel(diagnostic));
        await LoadSettingsAsync();
        await RefreshTasksAsync();
        await RefreshHistoryAsync();
        await RefreshQuarantineAsync();
        await RefreshSchedulesAsync();
        await RefreshAutoTriggerRulesAsync();
        await RefreshStorageConnectionsAsync();
        await RefreshIncompleteAsync();
        await RefreshNotificationChannelsAsync();
        await RefreshTemplatesAsync();
        if (CheckUpdatesOnStartup && !string.IsNullOrWhiteSpace(UpdateManifestUrl))
        {
            try { await CheckUpdateAsync(); } catch (Exception exception) { UpdateStatus = $"自动检查失败：{exception.Message}"; }
        }
    }

    public void SetAutoTriggerReload(Func<Task> reload) => _autoTriggerReload = reload;

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        _loadedSettings = settings;
        NotificationsEnabled = settings.NotificationsEnabled;
        MinimizeToTray = settings.MinimizeToTray;
        StartWithWindows = settings.StartWithWindows;
        LogRetentionDays = settings.LogRetentionDays;
        RetryDelaySeconds = settings.DefaultRetryDelaySeconds;
        AvailabilityWaitMinutes = settings.DefaultAvailabilityWaitMinutes;
        Theme = settings.Theme;
        Language = settings.Language;
        LogLevel = settings.LogLevel;
        UseSystemProxy = settings.UseSystemProxy;
        ProxyUrl = settings.ProxyUrl;
        ProxyUserName = settings.ProxyUserName;
        ProxyPassword = string.Empty;
        UpdateManifestUrl = settings.UpdateManifestUrl;
        CheckUpdatesOnStartup = settings.CheckUpdatesOnStartup;
        SelectedExistingFilePolicy = ExistingFilePolicies.First(option => option.Value == settings.DefaultExistingFilePolicy);
        SelectedVerification = VerificationOptions.First(option => option.Value == settings.DefaultVerificationMode);
        MaxAttempts = settings.DefaultRetryAttempts;
        MaximumDeleteItems = settings.DefaultMaximumDeleteItems;
        MaximumDeletePercentage = settings.DefaultMaximumDeletePercentage;
        BufferSizeKb = settings.DefaultBufferSizeKb;
        SpeedLimitMbPerSecond = settings.DefaultSpeedLimitMbPerSecond;
        MaxParallelFiles = settings.DefaultMaxParallelFiles;
        AutoTunePerformance = settings.DefaultAutoTunePerformance;
        BackgroundMaxParallelFiles = settings.BackgroundMaxParallelFiles;
        BackgroundSpeedLimitMbPerSecond = settings.BackgroundSpeedLimitMbPerSecond;
        SelectedPlanFilter = settings.PlanFilter;
        RecentPaths.Clear(); foreach (var path in settings.EffectiveRecentPaths) RecentPaths.Add(path);
        FavoritePaths.Clear(); foreach (var path in settings.EffectiveFavoritePaths) FavoritePaths.Add(path);
    }

    private async Task SaveSettingsAsync()
    {
        var settings = (_loadedSettings with
        {
            NotificationsEnabled = NotificationsEnabled,
            MinimizeToTray = MinimizeToTray,
            StartWithWindows = StartWithWindows,
            Theme = Theme,
            LogRetentionDays = LogRetentionDays,
            Language = Language,
            LogLevel = LogLevel,
            DefaultExistingFilePolicy = SelectedExistingFilePolicy.Value,
            DefaultVerificationMode = SelectedVerification.Value,
            DefaultRetryAttempts = MaxAttempts,
            DefaultRetryDelaySeconds = RetryDelaySeconds,
            DefaultAvailabilityWaitMinutes = AvailabilityWaitMinutes,
            DefaultMaximumDeleteItems = MaximumDeleteItems,
            DefaultMaximumDeletePercentage = MaximumDeletePercentage,
            DefaultBufferSizeKb = BufferSizeKb,
            DefaultSpeedLimitMbPerSecond = SpeedLimitMbPerSecond,
            DefaultMaxParallelFiles = MaxParallelFiles,
            DefaultAutoTunePerformance = AutoTunePerformance,
            BackgroundMaxParallelFiles = BackgroundMaxParallelFiles,
            BackgroundSpeedLimitMbPerSecond = BackgroundSpeedLimitMbPerSecond,
            UseSystemProxy = UseSystemProxy,
            ProxyUrl = ProxyUrl,
            ProxyUserName = ProxyUserName,
            RecentPaths = RecentPaths.ToArray(),
            FavoritePaths = FavoritePaths.ToArray(),
            UpdateManifestUrl = UpdateManifestUrl,
            CheckUpdatesOnStartup = CheckUpdatesOnStartup,
            PlanFilter = SelectedPlanFilter,
        }).Normalize();
        if (!string.IsNullOrEmpty(ProxyPassword))
        {
            _credentialStore?.Save(settings.ProxyCredentialTarget, ProxyUserName, ProxyPassword);
            ProxyPassword = string.Empty;
        }
        await _settingsStore.SaveAsync(settings);
        _loadedSettings = settings;
        _networkClients?.Update(settings);
        ThemeManager.Apply(settings.Theme);
        await _repository.DeleteRunsBeforeAsync(DateTime.UtcNow.AddDays(-LogRetentionDays));
        _startupRegistrationService.SetEnabled(
            StartWithWindows,
            Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 CopyTools 可执行文件路径。"));
        StatusMessage = "应用设置已保存。";
    }

    public async Task SaveWindowStateAsync(
        double width, double height, double left, double top, bool maximized, int selectedTabIndex,
        IReadOnlyList<double> planColumnWidths)
    {
        var settings = (await _settingsStore.LoadAsync()) with
        {
            RecentPaths = RecentPaths.ToArray(), FavoritePaths = FavoritePaths.ToArray(),
            WindowWidth = width, WindowHeight = height, WindowLeft = left, WindowTop = top,
            WindowMaximized = maximized, SelectedTabIndex = selectedTabIndex,
            PlanFilter = SelectedPlanFilter, PlanColumnWidths = planColumnWidths,
        };
        await _settingsStore.SaveAsync(settings);
        _loadedSettings = settings.Normalize();
    }

    private void AddStorageConnection() => ShowStorageConnectionDialog(null);

    private void EditStorageConnection()
    {
        if (SelectedStorageConnection is { } selected)
        {
            ShowStorageConnectionDialog(selected.Profile);
        }
    }

    private void ShowStorageConnectionDialog(StorageConnectionProfile? existing)
    {
        var dialog = new StorageConnectionDialog(existing, _pluginCatalog.StorageFactories)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() == true && dialog.Profile is { } profile)
        {
            _ = SaveStorageConnectionFromDialogAsync(profile, dialog.Secret);
        }
    }

    private async Task SaveStorageConnectionFromDialogAsync(StorageConnectionProfile profile, string? secret)
    {
        try
        {
            await _storageConnectionService.SaveAsync(profile, secret);
            await RefreshStorageConnectionsAsync();
            StatusMessage = $"存储连接“{profile.Name}”已保存；敏感凭据未写入配置数据库。";
        }
        catch (Exception exception)
        {
            HandleUnexpectedException(exception);
        }
    }

    private async Task DeleteStorageConnectionAsync()
    {
        var selected = SelectedStorageConnection;
        if (selected is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"删除存储连接“{selected.Name}”及其 Windows 安全凭据？",
                "删除存储连接", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await _storageConnectionService.DeleteAsync(selected.Profile);
        await RefreshStorageConnectionsAsync();
        StatusMessage = $"存储连接“{selected.Name}”及其安全凭据已删除。";
    }

    private async Task TestStorageConnectionAsync()
    {
        var selected = SelectedStorageConnection;
        if (selected is null)
        {
            return;
        }

        BeginOperation($"正在测试存储连接“{selected.Name}”…");
        try
        {
            var count = await _storageConnectionService.TestAsync(
                selected.Profile, _operationCancellation!.Token);
            StatusMessage = $"连接“{selected.Name}”测试成功，根目录返回 {count:N0} 个项目。";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RefreshStorageConnectionsAsync()
    {
        var selectedId = SelectedStorageConnection?.Profile.Id;
        var sourceId = SelectedTransferSource?.Profile.Id;
        var targetId = SelectedTransferTarget?.Profile.Id;
        StorageConnections.Clear();
        foreach (var profile in await _storageConnectionService.GetAllAsync())
        {
            StorageConnections.Add(new StorageConnectionViewModel(profile));
        }

        SelectedStorageConnection = StorageConnections.FirstOrDefault(item => item.Profile.Id == selectedId)
            ?? StorageConnections.FirstOrDefault();
        SelectedTransferSource = StorageConnections.FirstOrDefault(item => item.Profile.Id == sourceId)
            ?? StorageConnections.FirstOrDefault();
        SelectedTransferTarget = StorageConnections.FirstOrDefault(item => item.Profile.Id == targetId)
            ?? StorageConnections.Skip(1).FirstOrDefault()
            ?? StorageConnections.FirstOrDefault();
    }

    private void AddNotificationChannel() => ShowNotificationChannelDialog(null);

    private void EditNotificationChannel()
    {
        if (SelectedNotificationChannel is { } selected) ShowNotificationChannelDialog(selected.Channel);
    }

    private void ShowNotificationChannelDialog(NotificationChannel? existing)
    {
        if (_notificationChannelService is null) return;
        var dialog = new NotificationChannelDialog(existing)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() == true && dialog.Channel is { } channel)
            _ = SaveNotificationChannelAsync(channel, dialog.Secret);
    }

    private async Task SaveNotificationChannelAsync(NotificationChannel channel, string? secret)
    {
        try
        {
            await _notificationChannelService!.SaveAsync(channel, secret);
            await RefreshNotificationChannelsAsync();
            StatusMessage = $"通知频道“{channel.Name}”已保存。";
        }
        catch (Exception exception) { HandleUnexpectedException(exception); }
    }

    private async Task DeleteNotificationChannelAsync()
    {
        var selected = SelectedNotificationChannel;
        if (selected is null || _notificationChannelService is null) return;
        if (MessageBox.Show($"删除通知频道“{selected.Name}”及其安全凭据？", "删除通知频道",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await _notificationChannelService.DeleteAsync(selected.Channel);
        await RefreshNotificationChannelsAsync();
        StatusMessage = $"通知频道“{selected.Name}”已删除。";
    }

    private async Task TestNotificationChannelAsync()
    {
        var selected = SelectedNotificationChannel;
        if (selected is null || _notificationChannelService is null) return;
        BeginOperation($"正在测试通知频道“{selected.Name}”……");
        try
        {
            await _notificationChannelService.TestAsync(selected.Channel, _operationCancellation!.Token);
            StatusMessage = $"通知频道“{selected.Name}”测试消息已发送。";
        }
        finally { EndOperation(); }
    }

    private async Task RefreshNotificationChannelsAsync()
    {
        if (_notificationChannelService is null) return;
        var selectedId = SelectedNotificationChannel?.Channel.Id;
        NotificationChannels.Clear();
        foreach (var channel in await _notificationChannelService.GetAllAsync())
            NotificationChannels.Add(new NotificationChannelViewModel(channel));
        SelectedNotificationChannel = NotificationChannels.FirstOrDefault(item => item.Channel.Id == selectedId)
            ?? NotificationChannels.FirstOrDefault();
    }

    private FileFilterOptions BuildFilters() => new(
        SplitPatterns(IncludePatternsText), SplitPatterns(ExcludePatternsText),
        SplitPatterns(ExcludedDirectoriesText), ParseMegabytes(MinimumSizeMbText, "最小文件大小"),
        ParseMegabytes(MaximumSizeMbText, "最大文件大小"),
        ParseLocalDate(ModifiedAfterText, "最早修改时间"),
        ParseLocalDate(ModifiedBeforeText, "最晚修改时间", true),
        IncludeHiddenFiles, IncludeSystemFiles, SelectedReparsePoint.Value);

    private void ApplyFilters(FileFilterOptions filters)
    {
        IncludePatternsText = JoinPatterns(filters.EffectiveIncludePatterns);
        ExcludePatternsText = JoinPatterns(filters.EffectiveExcludePatterns);
        ExcludedDirectoriesText = JoinPatterns(filters.EffectiveExcludedDirectories);
        MinimumSizeMbText = FormatMegabytes(filters.MinimumBytes); MaximumSizeMbText = FormatMegabytes(filters.MaximumBytes);
        ModifiedAfterText = FormatLocalDate(filters.ModifiedAfterUtc); ModifiedBeforeText = FormatLocalDate(filters.ModifiedBeforeUtc);
        IncludeHiddenFiles = filters.IncludeHiddenFiles; IncludeSystemFiles = filters.IncludeSystemFiles;
        SelectedReparsePoint = ReparsePointOptions.First(option => option.Value == filters.ReparsePointPolicy);
    }

    private async Task RefreshTemplatesAsync()
    {
        FilterTemplates.Clear();
        foreach (var template in await _repository.GetFilterRuleTemplatesAsync()) FilterTemplates.Add(new FilterRuleTemplateViewModel(template));
        TaskTemplates.Clear();
        foreach (var template in await _repository.GetTaskTemplatesAsync()) TaskTemplates.Add(new TaskTemplateViewModel(template));
        SelectedFilterTemplate = FilterTemplates.FirstOrDefault(); SelectedTaskTemplate = TaskTemplates.FirstOrDefault();
    }

    private async Task SaveFilterTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTemplateName)) throw new CopyTaskValidationException("模板名称不能为空。");
        var now = DateTime.UtcNow;
        var existing = FilterTemplates.FirstOrDefault(item => string.Equals(item.Name, NewTemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
        await _repository.SaveFilterRuleTemplateAsync(new FilterRuleTemplate(existing?.Template.Id ?? Guid.NewGuid(),
            NewTemplateName.Trim(), BuildFilters(), existing?.Template.CreatedAtUtc ?? now, now));
        await RefreshTemplatesAsync(); StatusMessage = "过滤规则模板已保存。";
    }

    private void ApplyFilterTemplate() { if (SelectedFilterTemplate is { } item) ApplyFilters(item.Template.Filters); }
    private async Task DeleteFilterTemplateAsync() { if (SelectedFilterTemplate is { } item) { await _repository.DeleteFilterRuleTemplateAsync(item.Template.Id); await RefreshTemplatesAsync(); } }

    private async Task SaveTaskTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTemplateName)) throw new CopyTaskValidationException("模板名称不能为空。");
        var now = DateTime.UtcNow;
        var existing = TaskTemplates.FirstOrDefault(item => string.Equals(item.Name, NewTemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
        await _repository.SaveTaskTemplateAsync(new TaskTemplate(existing?.Template.Id ?? Guid.NewGuid(), NewTemplateName.Trim(),
            SelectedMode.Value, BuildFilters(), SelectedVerification.Value, new RetryPolicy(MaxAttempts, TimeSpan.FromSeconds(1)),
            new DeleteSafetyPolicy(MaximumDeleteItems, MaximumDeletePercentage), SelectedExistingFilePolicy.Value,
            new PerformanceOptions(BufferSizeKb * 1024, SpeedLimitMbPerSecond == 0 ? null : SpeedLimitMbPerSecond * 1024L * 1024L, MaxParallelFiles),
            existing?.Template.CreatedAtUtc ?? now, now));
        await RefreshTemplatesAsync(); StatusMessage = "任务模板已保存。";
    }

    private void ApplyTaskTemplate()
    {
        if (SelectedTaskTemplate is not { } item) return;
        var template = item.Template; SelectedMode = Modes.First(mode => mode.Value == template.Mode);
        ApplyFilters(template.Filters); SelectedVerification = VerificationOptions.First(option => option.Value == template.VerificationMode);
        MaxAttempts = template.RetryPolicy.MaxAttempts; MaximumDeleteItems = template.DeleteSafetyPolicy.MaximumItems;
        MaximumDeletePercentage = template.DeleteSafetyPolicy.MaximumPercentage;
        SelectedExistingFilePolicy = ExistingFilePolicies.First(option => option.Value == template.ExistingFilePolicy);
        BufferSizeKb = template.Performance.BufferSizeBytes / 1024;
        SpeedLimitMbPerSecond = template.Performance.SpeedLimitBytesPerSecond is null ? 0 : (int)(template.Performance.SpeedLimitBytesPerSecond.Value / 1024 / 1024);
        MaxParallelFiles = template.Performance.MaxParallelFiles;
    }

    private async Task DeleteTaskTemplateAsync() { if (SelectedTaskTemplate is { } item) { await _repository.DeleteTaskTemplateAsync(item.Template.Id); await RefreshTemplatesAsync(); } }

    private void BrowseDuplicateDirectory()
    {
        var selected = _folderPicker.PickFolder("添加重复文件扫描目录", string.Empty);
        if (selected is null) return;
        var paths = ParseSourcePaths(DuplicateScanPaths).ToList();
        if (!paths.Contains(selected, StringComparer.OrdinalIgnoreCase)) paths.Add(selected);
        DuplicateScanPaths = string.Join(Environment.NewLine, paths);
    }

    private async Task ScanDuplicatesAsync()
    {
        BeginOperation("正在扫描重复文件……");
        try
        {
            var groups = await new DuplicateFileDetector().ScanAsync(ParseSourcePaths(DuplicateScanPaths), BuildFilters(),
                new Progress<CopyProgress>(UpdateProgress), _operationCancellation!.Token);
            DuplicateGroups.Clear(); foreach (var group in groups) DuplicateGroups.Add(new DuplicateGroupViewModel(group));
            SelectedDuplicateGroup = DuplicateGroups.FirstOrDefault();
            DuplicateSummary = $"发现 {groups.Count:N0} 组重复文件，共 {groups.Sum(group => group.Files.Count):N0} 个；理论可释放 {FormatBytes(groups.Sum(group => group.ReclaimableBytes))}。未删除任何文件。";
            StatusMessage = "重复文件扫描完成。";
        }
        finally { EndOperation(); }
    }

    private bool CanTransferStorage() => !IsBusy
        && SelectedTransferSource is not null
        && SelectedTransferTarget is not null
        && SelectedTransferSource.Profile.Id != SelectedTransferTarget.Profile.Id;

    private void RefreshStorageTransferCommands()
    {
        StorageTransferSummary = "配置已变化，请重新预览。";
        PreviewStorageTransferCommand.NotifyCanExecuteChanged();
        ExecuteStorageTransferCommand.NotifyCanExecuteChanged();
    }

    private async Task PreviewStorageTransferAsync()
    {
        var sourceProfile = SelectedTransferSource?.Profile
            ?? throw new CopyTaskValidationException("请选择源存储连接。");
        var targetProfile = SelectedTransferTarget?.Profile
            ?? throw new CopyTaskValidationException("请选择目标存储连接。");
        BeginOperation("正在列举跨存储传输内容……");
        try
        {
            await using var source = _storageConnectionService.CreateAdapter(sourceProfile);
            await using var target = _storageConnectionService.CreateAdapter(targetProfile);
            var sourceItems = await source.ListAsync(
                StorageSourceDirectory, true, _operationCancellation!.Token);
            var files = sourceItems.Where(item => !item.IsDirectory).ToArray();
            var targetItems = await target.ListAsync(
                StorageTargetDirectory, true, _operationCancellation.Token);
            var targetPaths = targetItems.Where(item => !item.IsDirectory)
                .Select(item => item.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var collisions = files.Count(file => targetPaths.Contains(StoragePath.Combine(
                StorageTargetDirectory,
                RelativeStoragePath(file.RelativePath, StorageSourceDirectory))));
            StorageTransferSummary = $"将传输 {files.Length:N0} 个文件，共 {FormatBytes(files.Sum(item => item.Length))}；"
                + $"目标同名文件 {collisions:N0} 个。";
            StatusMessage = "跨存储传输预览完成。";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ExecuteStorageTransferAsync()
    {
        var sourceProfile = SelectedTransferSource?.Profile
            ?? throw new CopyTaskValidationException("请选择源存储连接。");
        var targetProfile = SelectedTransferTarget?.Profile
            ?? throw new CopyTaskValidationException("请选择目标存储连接。");
        var message = StorageTransferOverwrite
            ? "已启用覆盖。目标存储中的同名文件可能被替换，是否继续？"
            : "将开始跨存储传输；遇到同名文件会记录失败且不会覆盖。是否继续？";
        if (MessageBox.Show(message, "确认跨存储传输", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        BeginOperation("正在执行跨存储传输……");
        try
        {
            await using var source = _storageConnectionService.CreateAdapter(sourceProfile);
            await using var target = _storageConnectionService.CreateAdapter(targetProfile);
            var result = await _storageTransferService.CopyTreeAsync(
                source, target, StorageSourceDirectory, StorageTargetDirectory,
                StorageTransferOverwrite, progress: new Progress<CopyProgress>(UpdateProgress),
                cancellationToken: _operationCancellation!.Token);
            StorageTransferSummary = $"传输完成：成功 {result.SuccessfulFiles:N0}，失败 {result.Failures.Count:N0}，"
                + $"写入 {FormatBytes(result.CopiedBytes)}。";
            StatusMessage = result.IsSuccessful ? "跨存储传输成功。" : "跨存储传输完成，但存在失败项。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "跨存储传输已取消。";
        }
        finally
        {
            EndOperation();
        }
    }

    private static string RelativeStoragePath(string path, string root)
    {
        var normalizedPath = StoragePath.NormalizeRelative(path);
        var normalizedRoot = StoragePath.NormalizeRelative(root);
        return string.IsNullOrEmpty(normalizedRoot) ? normalizedPath
            : normalizedPath.StartsWith($"{normalizedRoot}/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath[(normalizedRoot.Length + 1)..]
                : normalizedPath;
    }

    private async Task ResetSettingsAsync()
    {
        var settings = new ApplicationSettings();
        await _settingsStore.SaveAsync(settings);
        _startupRegistrationService.SetEnabled(false, Environment.ProcessPath ?? string.Empty);
        await LoadSettingsAsync();
        StatusMessage = "应用设置已恢复默认值。";
    }

    private bool CanPreview() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(SourceDirectory)
        && !string.IsNullOrWhiteSpace(TargetDirectory);

    private bool CanSaveTask() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(TaskName)
        && !string.IsNullOrWhiteSpace(SourceDirectory)
        && !string.IsNullOrWhiteSpace(TargetDirectory);

    private bool CanSaveSchedule() => !IsBusy && SelectedTaskForSchedule is not null;

    private void BrowseSource()
    {
        var selected = _folderPicker.PickFolder("选择源目录", SourceDirectory);
        if (selected is not null)
        {
            AddSourcePaths([selected]);
        }
    }

    public void AddSourcePaths(IEnumerable<string> paths)
    {
        var combined = ParseSourcePaths(SourcePathsText).ToList();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var full = Path.GetFullPath(path);
            if (!combined.Contains(full, StringComparer.OrdinalIgnoreCase)) combined.Add(full);
        }
        SourcePathsText = string.Join(Environment.NewLine, combined);
    }

    public void AddSourceFiles()
    {
        var dialog = new OpenFileDialog { Title = "添加源文件", Multiselect = true, Filter = "所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog() == true) AddSourcePaths(dialog.FileNames);
    }

    public void ClearSourcePaths() => SourcePathsText = string.Empty;

    private void BrowseTarget()
    {
        var selected = _folderPicker.PickFolder("选择目标目录", TargetDirectory);
        if (selected is not null)
        {
            TargetDirectory = selected;
        }
    }

    private void AddCurrentPathsToFavorites()
    {
        AddFavorite(SourceDirectory);
        AddFavorite(TargetDirectory);
        StatusMessage = "当前有效路径已加入收藏；保存设置后持久化。";
    }

    private void AddFavorite(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || FavoritePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        FavoritePaths.Insert(0, path);
    }

    private void AddRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var existing = RecentPaths.FirstOrDefault(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) RecentPaths.Remove(existing);
        RecentPaths.Insert(0, path);
        while (RecentPaths.Count > 20) RecentPaths.RemoveAt(RecentPaths.Count - 1);
    }

    private void UseQuickPath(string? path, bool source)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (source) SourceDirectory = path; else TargetDirectory = path;
    }

    private void RemoveFavorite()
    {
        if (SelectedFavoritePath is { } path) FavoritePaths.Remove(path);
        SelectedFavoritePath = FavoritePaths.FirstOrDefault();
    }

    private void BrowseBackupSource()
    {
        var selected = _folderPicker.PickFolder("选择备份源目录", BackupSourceDirectory);
        if (selected is not null)
        {
            BackupSourceDirectory = selected;
        }
    }

    private void BrowseBackupRepository()
    {
        var selected = _folderPicker.PickFolder("选择备份版本库目录", BackupRepositoryDirectory);
        if (selected is not null)
        {
            BackupRepositoryDirectory = selected;
        }
    }

    private void BrowseBackupRestore()
    {
        var selected = _folderPicker.PickFolder("选择恢复目标目录", BackupRestoreDirectory);
        if (selected is not null)
        {
            BackupRestoreDirectory = selected;
        }
    }

    private async Task CreateSecureArchiveAsync()
    {
        var source = _folderPicker.PickFolder("选择要打包的源目录", BackupSourceDirectory);
        if (source is null) return;
        var options = new SecureArchiveOptionsDialog(false)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (options.ShowDialog() != true) return;
        var dialog = new SaveFileDialog
        {
            Title = "保存安全归档",
            Filter = options.Encrypt
                ? "CopyTools 加密归档 (*.ctbackup)|*.ctbackup"
                : "ZIP 归档 (*.zip)|*.zip",
            FileName = $"backup-{DateTime.Now:yyyyMMdd-HHmmss}{(options.Encrypt ? ".ctbackup" : ".zip")}",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true) return;

        BeginOperation("正在创建安全归档……");
        try
        {
            var result = await _secureArchiveService.CreateAsync(
                source, dialog.FileName, options.Compress, options.Password,
                new Progress<CopyProgress>(UpdateProgress), _operationCancellation!.Token);
            StatusMessage = $"安全归档已创建：{result.FileCount:N0} 个文件，"
                + $"归档大小 {FormatBytes(result.ArchiveBytes)}，加密：{(result.Encrypted ? "是" : "否")}。";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RestoreSecureArchiveAsync()
    {
        var open = new OpenFileDialog
        {
            Title = "选择安全归档",
            Filter = "CopyTools / ZIP 归档 (*.ctbackup;*.zip)|*.ctbackup;*.zip|所有文件 (*.*)|*.*",
            Multiselect = false,
        };
        if (open.ShowDialog() != true) return;
        var target = _folderPicker.PickFolder("选择安全归档恢复目录", BackupRestoreDirectory);
        if (target is null) return;
        var encrypted = string.Equals(Path.GetExtension(open.FileName), ".ctbackup", StringComparison.OrdinalIgnoreCase);
        var options = new SecureArchiveOptionsDialog(true, encrypted)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (options.ShowDialog() != true) return;

        BeginOperation("正在验证并恢复安全归档……");
        try
        {
            await _secureArchiveService.RestoreAsync(
                open.FileName, target, options.Password, options.Overwrite, _operationCancellation!.Token);
            StatusMessage = $"安全归档已恢复到：{target}";
        }
        finally
        {
            EndOperation();
        }
    }

    private bool CanCreateBackup() => !IsBusy
        && !string.IsNullOrWhiteSpace(BackupSourceDirectory)
        && !string.IsNullOrWhiteSpace(BackupRepositoryDirectory);

    private async Task CreateBackupAsync()
    {
        BeginOperation("正在创建备份快照……");
        try
        {
            var progress = new Progress<CopyProgress>(UpdateProgress);
            var request = new BackupTaskRequest(
                    BackupSourceDirectory,
                    BackupRepositoryDirectory,
                    SelectedBackupMode.Value,
                    VerificationMode: SelectedVerification.Value);
            var result = BackupUseVss
                ? await _vssBackupService.CreateBackupAsync(request, progress, _operationCancellation!.Token)
                : await _backupService.CreateBackupAsync(request, progress, _operationCancellation!.Token);
            StatusMessage = $"备份完成：{result.Snapshot.Files.Count:N0} 个文件；"
                + $"新增对象 {result.NewObjects:N0}，复用对象 {result.ReusedObjects:N0}。";
            await RefreshBackupSnapshotsAsync();
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RefreshBackupSnapshotsAsync()
    {
        var snapshots = await _backupService.GetSnapshotsAsync(BackupRepositoryDirectory);
        BackupSnapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            BackupSnapshots.Add(new BackupSnapshotViewModel(snapshot));
        }

        SelectedBackupSnapshot = BackupSnapshots.FirstOrDefault();
    }

    private async Task RestoreBackupAsync()
    {
        var snapshot = SelectedBackupSnapshot?.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        var selected = BackupFiles.Where(file => file.IsSelected).Select(file => file.RelativePath).ToArray();
        var paths = selected.Length == 0 ? null : selected;
        BeginOperation("正在恢复备份快照……");
        try
        {
            await _backupService.RestoreAsync(
                BackupRepositoryDirectory,
                snapshot,
                BackupRestoreDirectory,
                paths,
                BackupRestoreOverwrite,
                _operationCancellation!.Token);
            StatusMessage = paths is null
                ? $"快照已完整恢复到：{BackupRestoreDirectory}"
                : $"已恢复所选 {paths.Length:N0} 个文件到：{BackupRestoreDirectory}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task CleanupBackupsAsync()
    {
        var deleted = await _backupService.ApplyRetentionAsync(
            BackupRepositoryDirectory,
            BackupKeepLatest);
        await RefreshBackupSnapshotsAsync();
        StatusMessage = $"备份保留策略完成：删除 {deleted:N0} 个旧快照，仍被引用的数据对象已保留。";
    }

    private async Task PreviewAsync()
    {
        BeginOperation("正在扫描源目录并生成操作计划……");
        try
        {
            var plan = await _workflow.CreatePlanAsync(
                PathTemplateExpander.Expand(BuildRequest(), TaskName),
                _operationCancellation!.Token);
            CurrentPlan = plan;
            if (plan.UnresolvedConflicts > 0 && !ResolveInteractiveConflicts())
            {
                CurrentPlan = null;
                StatusMessage = "冲突确认已取消，未生成可执行计划。";
                return;
            }
            plan = CurrentPlan!;
            AddRecent(plan.Request.SourceDirectory);
            AddRecent(plan.Request.TargetDirectory);
            PlanItems.Clear();
            foreach (var item in plan.Items)
            {
                PlanItems.Add(new PlanItemViewModel(item));
            }

            StatusMessage = plan.OperationsToExecute == 0
                ? plan.Request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
                    ? "扫描完成，没有文件变化；可以执行以提交或刷新同步基线。"
                    : "扫描完成，没有需要执行的操作。"
                : "预览已生成。请确认新增和覆盖内容后再开始执行。";
        }
        catch (CopyTaskValidationException exception)
        {
            ShowWarning(exception.Message);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "扫描已取消。";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task ExecuteAsync()
    {
        var plan = CurrentPlan;
        if (plan is null)
        {
            return;
        }

        if (plan.FilesToOverwrite > 0)
        {
            var answer = MessageBox.Show(
                $"任务将覆盖 {plan.FilesToOverwrite:N0} 个已有文件。\n\n是否确认执行？",
                "确认覆盖",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (plan.FilesToQuarantine > 0 || plan.SourceFilesToRemove > 0)
        {
            var answer = MessageBox.Show(
                $"任务包含高风险文件操作：\n\n"
                + $"• 目标文件移入隔离区：{plan.TargetFilesToQuarantine:N0} 个\n"
                + $"• 源端文件移入隔离区：{plan.SourceFilesToQuarantine:N0} 个\n"
                + $"• 源文件移入隔离区：{plan.SourceFilesToRemove:N0} 个\n\n"
                + "文件不会永久删除，可以从隔离区恢复。是否确认执行？",
                "确认隔离操作",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        BeginOperation("复制任务正在执行……");
        try
        {
            _executionControl = new CopyExecutionControl();
            var progress = new Progress<CopyProgress>(UpdateProgress);
            CopyExecutionResult result;
            var executedPlan = plan;
            if (_currentTaskId is { } taskId
                && await _repository.GetTaskAsync(taskId, _operationCancellation!.Token) is { } savedTask)
            {
                (executedPlan, result) = await _resumableExecutionService.ExecuteAsync(
                    savedTask with { Request = plan.Request },
                    plan,
                    progress,
                    _operationCancellation.Token,
                    _executionControl);
            }
            else
            {
                result = await _workflow.ExecuteAsync(
                    plan,
                    progress,
                    _operationCancellation!.Token,
                    _executionControl);
            }
            await SaveRunHistoryAsync(executedPlan, result);
            _lastExecutedPlan = executedPlan;
            _lastExecutionResult = result;
            RetryFailuresCommand.NotifyCanExecuteChanged();

            StatusMessage = result.WasCancelled
                ? $"任务已取消，取消前成功复制 {result.SuccessfulFiles:N0} 个文件。"
                : result.IsSuccessful
                    ? $"任务成功：复制 {result.SuccessfulFiles:N0} 个文件，共 {FormatBytes(result.CopiedBytes)}。"
                    : $"任务{result.Status}：成功 {result.SuccessfulFiles:N0} 个，失败 {result.Failures.Count:N0} 个。";
            await NotifyTaskResultAsync(
                _currentTaskId ?? Guid.Empty,
                string.IsNullOrWhiteSpace(TaskName) ? "临时任务" : TaskName.Trim(),
                result);
            TaskFinished?.Invoke(this, new TaskResultNotification(
                _currentTaskId ?? Guid.Empty,
                string.IsNullOrWhiteSpace(TaskName) ? "临时任务" : TaskName.Trim(),
                result.Status, result.CompletedAtUtc, result.SuccessfulFiles, result.Failures.Count, result.CopiedBytes));

            if (result.Failures.Count > 0)
            {
                var details = string.Join(
                    Environment.NewLine,
                    result.Failures.Take(8).Select(failure =>
                        $"• {failure.RelativePath}（尝试 {failure.Attempts} 次）: {failure.ErrorMessage}"));
                MessageBox.Show(
                    $"部分文件复制失败：\n\n{details}",
                    "任务结果",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            CurrentPlan = null;
            PlanItems.Clear();
            await RefreshHistoryAsync();
            await RefreshQuarantineAsync();
        }
        finally
        {
            _executionControl = null;
            EndOperation();
        }
    }

    private void ExcludeSelectedPlanItems()
    {
        if (CurrentPlan is null)
        {
            return;
        }

        var indexes = PlanItems
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.IsSelected)
            .Select(pair => pair.index)
            .ToArray();
        CurrentPlan = PlanEditor.ExcludeItems(CurrentPlan, indexes);
        PlanItems.Clear();
        foreach (var item in CurrentPlan.Items)
        {
            PlanItems.Add(new PlanItemViewModel(item));
        }

        StatusMessage = $"已从本次计划排除 {indexes.Length:N0} 项；任务配置本身未改变。";
        ExcludeSelectedPlanItemsCommand.NotifyCanExecuteChanged();
    }

    private async Task RetryFailuresAsync()
    {
        if (_lastExecutedPlan is null || _lastExecutionResult is null)
        {
            return;
        }

        BeginOperation("正在重试上次失败的文件……");
        try
        {
            _executionControl = new CopyExecutionControl();
            var progress = new Progress<CopyProgress>(UpdateProgress);
            var result = await _workflow.RetryFailuresAsync(
                _lastExecutedPlan,
                _lastExecutionResult,
                progress,
                _operationCancellation!.Token,
                _executionControl);
            await SaveRunHistoryAsync(_lastExecutedPlan, result);
            _lastExecutionResult = result;
            StatusMessage = result.IsSuccessful
                ? "失败项已全部重试成功。"
                : $"重试完成，仍有 {result.Failures.Count:N0} 个失败项。";
            await RefreshHistoryAsync();
        }
        finally
        {
            _executionControl = null;
            EndOperation();
            RetryFailuresCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SaveTaskAsync()
    {
        var now = DateTime.UtcNow;
        var taskId = _currentTaskId ?? Guid.NewGuid();
        var task = new SavedCopyTask(
            taskId,
            TaskName.Trim(),
            BuildRequest(taskId),
            _currentTaskCreatedAtUtc ?? now,
            now);
        await _repository.SaveTaskAsync(task);
        _currentTaskId = task.Id;
        _currentTaskCreatedAtUtc = task.CreatedAtUtc;
        await RefreshTasksAsync();
        SelectedSavedTask = SavedTasks.FirstOrDefault(item => item.Task.Id == task.Id);
        StatusMessage = $"任务“{task.Name}”已保存。";
    }

    private void StartNewTask(CopyMode mode)
    {
        _currentTaskId = null;
        _currentTaskCreatedAtUtc = null;
        TaskName = mode == CopyMode.FullCopy ? "新建复制任务" : "新建同步任务";
        SelectedMode = Modes.First(option => option.Value == mode);
        CurrentPlan = null;
        PlanItems.Clear();
        StatusMessage = "已创建空白任务，请选择源路径和目标目录。";
    }

    private void LoadSelectedTask()
    {
        var saved = SelectedSavedTask?.Task;
        if (saved is null)
        {
            return;
        }

        _currentTaskId = saved.Id;
        _currentTaskCreatedAtUtc = saved.CreatedAtUtc;
        TaskName = saved.Name;
        ApplyRequest(saved.Request);
        StatusMessage = $"已加载任务“{saved.Name}”，请生成预览后执行。";
    }

    private async Task DeleteSelectedTaskAsync()
    {
        var saved = SelectedSavedTask?.Task;
        if (saved is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"确定删除任务“{saved.Name}”吗？\n\n已有运行历史不会被删除。",
            "删除任务",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _repository.DeleteTaskAsync(saved.Id);
        if (_currentTaskId == saved.Id)
        {
            _currentTaskId = null;
            _currentTaskCreatedAtUtc = null;
        }

        await RefreshTasksAsync();
        StatusMessage = $"任务“{saved.Name}”已删除。";
    }

    private async Task DuplicateSelectedTaskAsync()
    {
        var saved = SelectedSavedTask?.Task;
        if (saved is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var request = saved.Request.Mode is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
            ? saved.Request with { SynchronizationId = id }
            : saved.Request;
        await _repository.SaveTaskAsync(new SavedCopyTask(id, $"{saved.Name} - 副本", request, now, now));
        await RefreshTasksAsync();
        SelectedSavedTask = SavedTasks.First(item => item.Task.Id == id);
        StatusMessage = $"已复制任务“{saved.Name}”。";
    }

    private async Task ExportTasksAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 CopyTools 任务配置",
            Filter = "CopyTools 任务配置 (*.copytools.json)|*.copytools.json|JSON 文件 (*.json)|*.json",
            FileName = $"copytools-tasks-{DateTime.Now:yyyyMMdd-HHmmss}.copytools.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _taskTransferService.ExportAsync(dialog.FileName);
        StatusMessage = $"任务配置已导出到：{dialog.FileName}";
    }

    private async Task ImportTasksAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 CopyTools 任务配置",
            Filter = "CopyTools 任务配置 (*.copytools.json;*.json)|*.copytools.json;*.json|所有文件 (*.*)|*.*",
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var count = await _taskTransferService.ImportAsync(dialog.FileName);
        await RefreshTasksAsync();
        await RefreshSchedulesAsync();
        StatusMessage = $"成功导入 {count:N0} 个任务。";
    }

    private async Task ExportConfigurationAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "备份 CopyTools 完整配置",
            Filter = "CopyTools 配置备份 (*.copytools-config.json)|*.copytools-config.json|JSON 文件 (*.json)|*.json",
            FileName = $"copytools-config-{DateTime.Now:yyyyMMdd-HHmmss}.copytools-config.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true) return;
        await _configurationBackupService.ExportAsync(dialog.FileName);
        StatusMessage = $"配置已备份（不含密码和令牌）：{dialog.FileName}";
    }

    private async Task CheckUpdateAsync()
    {
        BeginOperation("正在检查更新……");
        try
        {
            var current = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var result = await _updateService.CheckAsync(UpdateManifestUrl, current, _operationCancellation!.Token);
            _availableUpdate = result.IsUpdateAvailable ? result.Manifest : null;
            DownloadUpdateCommand.NotifyCanExecuteChanged();
            UpdateStatus = result.IsUpdateAvailable
                ? $"发现新版本 {result.LatestVersion}（当前 {result.CurrentVersion}），大小 {FormatBytes(result.Manifest.Size)}。"
                : $"当前已是最新版本 {result.CurrentVersion}。";
            StatusMessage = UpdateStatus;
        }
        finally { EndOperation(); }
    }

    private async Task DownloadUpdateAsync()
    {
        var manifest = _availableUpdate;
        if (manifest is null) return;
        var suggestedName = Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.LocalPath) : Path.GetFileName(manifest.DownloadUrl);
        var dialog = new SaveFileDialog
        {
            Title = "保存 CopyTools 更新包",
            Filter = "ZIP 更新包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(suggestedName) ? $"CopyTools-{manifest.Version}.zip" : suggestedName,
        };
        if (dialog.ShowDialog() != true) return;
        BeginOperation("正在下载并校验更新包……");
        try
        {
            var path = await _updateService.DownloadAsync(manifest, dialog.FileName,
                new Progress<CopyProgress>(UpdateProgress), _operationCancellation!.Token);
            UpdateStatus = $"更新包已下载并通过 SHA-256 校验：{path}";
            StatusMessage = UpdateStatus;
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
        }
        finally { EndOperation(); }
    }

    private async Task RestoreConfigurationAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "恢复 CopyTools 完整配置",
            Filter = "CopyTools 配置备份 (*.copytools-config.json;*.json)|*.copytools-config.json;*.json|所有文件 (*.*)|*.*",
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;
        var choice = MessageBox.Show(
            "选择“是”将替换现有任务与连接配置；选择“否”将合并并自动避开同 ID 项；选择“取消”停止。\n\n运行历史、隔离区和 Windows 安全存储中的凭据不会删除。",
            "配置恢复方式",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return;
        var result = await _configurationBackupService.ImportAsync(dialog.FileName, choice == MessageBoxResult.Yes);
        await LoadSettingsAsync();
        _networkClients?.Update(_loadedSettings);
        ThemeManager.Apply(_loadedSettings.Theme);
        await RefreshTasksAsync();
        await RefreshSchedulesAsync();
        await RefreshAutoTriggerRulesAsync();
        await RefreshStorageConnectionsAsync();
        await RefreshNotificationChannelsAsync();
        await RefreshTemplatesAsync();
        StatusMessage = $"配置已{(result.ReplacedExisting ? "替换恢复" : "合并恢复")}：任务 {result.Tasks:N0}、连接 {result.StorageConnections:N0}、通知 {result.NotificationChannels:N0}、模板 {result.Templates:N0}。密码和令牌需在本机安全存储中另行配置。";
    }

    private async Task ExportSelectedRunLogAsync()
    {
        var run = SelectedRun?.Run;
        if (run is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出运行日志",
            Filter = "JSON 文件 (*.json)|*.json|CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
            FileName = $"copytools-run-{run.CompletedAtUtc.ToLocalTime():yyyyMMdd-HHmmss}.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        switch (Path.GetExtension(dialog.FileName).ToLowerInvariant())
        {
            case ".csv":
                await _runLogExportService.ExportCsvAsync(run, dialog.FileName);
                break;
            case ".txt":
                await _runLogExportService.ExportTextAsync(run, dialog.FileName);
                break;
            default:
                await _runLogExportService.ExportJsonAsync(run, dialog.FileName);
                break;
        }

        StatusMessage = $"运行日志已导出到：{dialog.FileName}";
    }

    private async Task RefreshTasksAsync()
    {
        var tasks = await _repository.GetTasksAsync();
        var options = (await _repository.GetAllTaskExecutionOptionsAsync())
            .ToDictionary(item => item.TaskId);
        var selectedIds = SavedTasks.Where(item => item.IsQueueSelected)
            .Select(item => item.Task.Id).ToHashSet();
        SavedTasks.Clear();
        foreach (var task in tasks)
        {
            var latestRun = await _repository.GetLatestRunForTaskAsync(task.Id);
            var item = new SavedTaskViewModel(task, options.GetValueOrDefault(task.Id), latestRun)
            {
                IsQueueSelected = selectedIds.Contains(task.Id),
            };
            SavedTasks.Add(item);
        }

        SelectedTaskForSchedule ??= SavedTasks.FirstOrDefault();
        SavedTasksChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ScheduledRunResult> RunSavedTaskFromTrayAsync(Guid taskId)
    {
        var task = await _repository.GetTaskAsync(taskId)
            ?? throw new InvalidOperationException("所选任务已不存在。");
        var destructive = task.Request.Mode is CopyMode.FullCopy or CopyMode.MirrorSync
            or CopyMode.MoveSync or CopyMode.TwoWaySync;
        if (destructive)
        {
            var answer = MessageBox.Show(
                $"将从托盘立即运行“{task.Name}”。该任务可能覆盖或隔离文件，仍会执行计划安全阈值检查。\n\n是否继续？",
                "运行已保存任务", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return new ScheduledRunResult(task.Id, task.Name, "已取消", false, DateTime.UtcNow);
        }
        var schedule = new TaskSchedule(task.Id, true, ScheduleTrigger.Interval, TimeSpan.Zero,
            AllowOverwrite: destructive, AllowQuarantine: destructive);
        return await _scheduledTaskRunner.RunAsync(schedule);
    }

    private void LoadOrchestration(TaskExecutionOptions? options)
    {
        options ??= new TaskExecutionOptions(SelectedSavedTask?.Task.Id ?? Guid.Empty);
        OrchestrationDescription = options.Description;
        OrchestrationGroup = options.Group;
        OrchestrationTags = string.Join("; ", options.EffectiveTags);
        OrchestrationPriority = options.Priority;
        OrchestrationDependencies = string.Join("; ", options.EffectiveDependencies);
        BeforeCommandPath = options.BeforeCommand?.FileName ?? string.Empty;
        BeforeCommandArguments = JoinCommandArguments(options.BeforeCommand?.EffectiveArguments ?? []);
        AfterCommandPath = options.AfterCommand?.FileName ?? string.Empty;
        AfterCommandArguments = JoinCommandArguments(options.AfterCommand?.EffectiveArguments ?? []);
        RunAfterCommandOnFailure = options.RunAfterCommandOnFailure;
    }

    private async Task SaveOrchestrationAsync()
    {
        var task = SelectedSavedTask?.Task
            ?? throw new InvalidOperationException("请选择一个已保存任务。");
        var dependencies = new List<Guid>();
        foreach (var token in SplitPatterns(OrchestrationDependencies))
        {
            var matched = SavedTasks.FirstOrDefault(item =>
                string.Equals(item.Name, token, StringComparison.OrdinalIgnoreCase));
            if (matched is not null) dependencies.Add(matched.Task.Id);
            else if (Guid.TryParse(token, out var id)) dependencies.Add(id);
            else throw new CopyTaskValidationException($"无法识别依赖任务：{token}。请填写任务名称或任务 ID。");
        }
        if (dependencies.Contains(task.Id))
            throw new CopyTaskValidationException("任务不能依赖自身。");

        var options = new TaskExecutionOptions(
            task.Id,
            OrchestrationDescription,
            OrchestrationGroup,
            SplitPatterns(OrchestrationTags),
            OrchestrationPriority,
            dependencies,
            BuildControlledCommand(BeforeCommandPath, BeforeCommandArguments),
            BuildControlledCommand(AfterCommandPath, AfterCommandArguments),
            RunAfterCommandOnFailure).Normalize();
        await _repository.SaveTaskExecutionOptionsAsync(options);
        await RefreshTasksAsync();
        SelectedSavedTask = SavedTasks.FirstOrDefault(item => item.Task.Id == task.Id);
        StatusMessage = $"任务“{task.Name}”的编排配置已保存。";
    }

    private async Task RunTaskQueueAsync()
    {
        var selected = SavedTasks.Where(item => item.IsQueueSelected).Select(item => item.Task.Id).ToArray();
        if (selected.Length == 0) return;
        if (MessageBox.Show(
                $"将运行 {selected.Length:N0} 个任务。队列会按依赖和优先级调度，并允许自动覆盖及隔离。是否继续？",
                "确认运行任务队列", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        BeginOperation("正在运行任务队列……");
        try
        {
            var result = await _taskQueueService.RunAsync(
                selected, MaximumParallelTasks, _operationCancellation!.Token);
            QueueSummary = $"队列完成：{result.Items.Count(item => item.Status == "成功"):N0} 成功，"
                + $"{result.Items.Count(item => item.Status != "成功"):N0} 未成功。"
                + string.Join(" ", result.Items.Select(item => $"{item.TaskName}={item.Status}"));
            StatusMessage = result.IsSuccessful ? "任务队列全部成功。" : "任务队列已完成，请查看结果摘要。";
            await RefreshHistoryAsync();
            await RefreshQuarantineAsync();
            await RefreshIncompleteAsync();
        }
        finally
        {
            EndOperation();
        }
    }

    private static ControlledCommand? BuildControlledCommand(string path, string arguments)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmedPath = path.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
            throw new CopyTaskValidationException("前后置命令必须填写绝对可执行文件路径。");
        var fullPath = Path.GetFullPath(trimmedPath);
        return new ControlledCommand(fullPath, ParseCommandArguments(arguments));
    }

    private static IReadOnlyList<string> ParseCommandArguments(string value)
    {
        var output = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0) { output.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(character);
        }
        if (quoted) throw new CopyTaskValidationException("命令参数中的双引号未闭合。");
        if (current.Length > 0) output.Add(current.ToString());
        return output;
    }

    private static string JoinCommandArguments(IReadOnlyList<string> arguments) => string.Join(" ", arguments.Select(argument =>
        argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument));

    private async Task RefreshIncompleteAsync()
    {
        var selectedId = SelectedIncompleteExecution?.Execution.Id;
        var tasks = (await _repository.GetTasksAsync()).ToDictionary(task => task.Id);
        IncompleteExecutions.Clear();
        foreach (var execution in await _resumableExecutionService.GetIncompleteAsync())
        {
            var name = tasks.TryGetValue(execution.TaskId, out var task)
                ? task.Name
                : "任务已删除";
            IncompleteExecutions.Add(new IncompleteExecutionViewModel(execution, name));
        }
        SelectedIncompleteExecution = IncompleteExecutions.FirstOrDefault(item => item.Execution.Id == selectedId)
            ?? IncompleteExecutions.FirstOrDefault();
    }

    private async Task ResumeIncompleteAsync()
    {
        var incomplete = SelectedIncompleteExecution?.Execution;
        if (incomplete is null) return;
        var task = await _repository.GetTaskAsync(incomplete.TaskId)
            ?? throw new InvalidOperationException("未完成执行关联的任务已删除，无法继续。");
        if (MessageBox.Show(
                "CopyTools 会重新扫描源和目标，只跳过来源与目标仍完全匹配的已完成项；"
                + "覆盖、隔离等操作将根据当前状态重新计算。是否继续？",
                "继续未完成任务", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        BeginOperation($"正在继续任务“{task.Name}”……");
        try
        {
            _executionControl = new CopyExecutionControl();
            var (plan, result) = await _resumableExecutionService.ResumeAsync(
                task.Id, new Progress<CopyProgress>(UpdateProgress), _operationCancellation!.Token,
                _executionControl);
            await _repository.SaveRunAsync(RunHistoryFactory.FromExecution(task, plan, result));
            StatusMessage = result.WasCancelled
                ? $"任务“{task.Name}”再次中断，已更新恢复检查点。"
                : result.IsSuccessful
                    ? $"任务“{task.Name}”已恢复并执行成功。"
                    : $"任务“{task.Name}”恢复执行完成，失败 {result.Failures.Count:N0} 项。";
            await RefreshIncompleteAsync();
            await RefreshHistoryAsync();
            await RefreshQuarantineAsync();
        }
        finally
        {
            _executionControl = null;
            EndOperation();
        }
    }

    private async Task RefreshSchedulesAsync()
    {
        var schedules = await _repository.GetSchedulesAsync();
        var tasks = (await _repository.GetTasksAsync()).ToDictionary(task => task.Id);
        Schedules.Clear();
        foreach (var schedule in schedules)
        {
            if (tasks.TryGetValue(schedule.TaskId, out var task))
            {
                Schedules.Add(new ScheduledTaskViewModel(task, schedule));
            }
        }

        SelectedSchedule = Schedules.FirstOrDefault();
    }

    private async Task SaveScheduleAsync()
    {
        var task = SelectedTaskForSchedule?.Task;
        if (task is null)
        {
            return;
        }

        if (!TimeSpan.TryParseExact(
                ScheduleTimeText,
                ["h\\:mm", "hh\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss"],
                CultureInfo.InvariantCulture,
                out var localTime))
        {
            throw new CopyTaskValidationException("计划时间格式应为 HH:mm，例如 02:30。");
        }

        var now = DateTime.UtcNow;
        var schedule = new TaskSchedule(
            task.Id,
            ScheduleEnabled,
            SelectedScheduleTrigger.Value,
            localTime,
            SelectedScheduleDay.Value,
            ScheduleDayOfMonth,
            TimeSpan.FromMinutes(ScheduleIntervalMinutes),
            ScheduleRunMissed ? MissedRunPolicy.RunImmediately : MissedRunPolicy.Skip,
            ScheduleAllowOverwrite,
            ScheduleAllowQuarantine);
        var nextRun = schedule.Enabled
            ? schedule.Trigger == ScheduleTrigger.ApplicationStartup
                ? now
                : _scheduleCalculator.CalculateNextUtc(schedule, now)
            : (DateTime?)null;
        await _repository.SaveScheduleAsync(schedule with { NextRunAtUtc = nextRun });
        await RefreshSchedulesAsync();
        StatusMessage = $"任务“{task.Name}”的计划已保存。";
    }

    private async Task DeleteSelectedScheduleAsync()
    {
        var selected = SelectedSchedule;
        if (selected is null)
        {
            return;
        }

        await _repository.DeleteScheduleAsync(selected.Schedule.TaskId);
        await RefreshSchedulesAsync();
        StatusMessage = $"任务“{selected.Name}”的计划已删除。";
    }

    private async Task SaveAutoTriggerAsync()
    {
        var task = SelectedTaskForSchedule?.Task;
        if (task is null)
        {
            return;
        }

        await _repository.SaveAutoTriggerRuleAsync(new AutoTriggerRule(
            task.Id,
            AutoTriggerFileChange ? AutoTriggerKind.FileChange : AutoTriggerKind.PathAvailable,
            AutoTriggerEnabled,
            TimeSpan.FromSeconds(AutoTriggerDelaySeconds),
            AutoTriggerAllowOverwrite,
            AutoTriggerAllowQuarantine));
        await RefreshAutoTriggerRulesAsync();
        if (_autoTriggerReload is not null) await _autoTriggerReload();
        StatusMessage = $"任务“{task.Name}”的自动触发规则已保存并立即生效。";
    }

    private async Task DeleteAutoTriggerAsync()
    {
        var selected = SelectedAutoTriggerRule;
        if (selected is null)
        {
            return;
        }

        await _repository.DeleteAutoTriggerRuleAsync(selected.Rule.TaskId);
        await RefreshAutoTriggerRulesAsync();
        if (_autoTriggerReload is not null) await _autoTriggerReload();
        StatusMessage = $"任务“{selected.Name}”的自动触发规则已删除。";
    }

    private async Task RefreshAutoTriggerRulesAsync()
    {
        var tasks = (await _repository.GetTasksAsync()).ToDictionary(task => task.Id);
        var rules = await _repository.GetAutoTriggerRulesAsync();
        AutoTriggerRules.Clear();
        foreach (var rule in rules)
        {
            if (tasks.TryGetValue(rule.TaskId, out var task))
            {
                AutoTriggerRules.Add(new AutoTriggerRuleViewModel(task, rule));
            }
        }

        SelectedAutoTriggerRule = AutoTriggerRules.FirstOrDefault();
    }

    private async Task RunSelectedScheduleNowAsync()
    {
        var selected = SelectedSchedule;
        if (selected is null)
        {
            return;
        }

        BeginOperation($"正在自动执行任务“{selected.Name}”……");
        try
        {
            var result = await _scheduledTaskRunner.RunAsync(
                selected.Schedule,
                _operationCancellation!.Token);
            StatusMessage = result.Message is null
                ? $"计划任务“{result.TaskName}”：{result.Status}。"
                : $"计划任务“{result.TaskName}”：{result.Status}；{result.Message}";
            TaskFinished?.Invoke(this, new TaskResultNotification(
                result.TaskId, result.TaskName, result.Status, result.CompletedAtUtc,
                result.ExecutionResult?.SuccessfulFiles ?? 0,
                result.ExecutionResult?.Failures.Count ?? (result.Status == "失败" ? 1 : 0),
                result.ExecutionResult?.CopiedBytes ?? 0,
                result.Message));
            await RefreshHistoryAsync();
            await RefreshQuarantineAsync();
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task RefreshHistoryAsync()
    {
        var runs = await _repository.GetRecentRunsAsync(200);
        RunHistory.Clear();
        foreach (var run in runs)
        {
            RunHistory.Add(new RunHistoryViewModel(run));
        }

        SelectedRun = RunHistory.FirstOrDefault();
    }

    private async Task QueryHistoryAsync()
    {
        var runs = await _repository.QueryRunsAsync(new RunHistoryQuery(
            HistoryTaskFilter,
            HistoryStatusFilter,
            ParseLocalDate(HistoryFromText, "历史开始日期"),
            ParseLocalDate(HistoryToText, "历史结束日期", true),
            500));
        RunHistory.Clear(); foreach (var run in runs) RunHistory.Add(new RunHistoryViewModel(run));
        SelectedRun = RunHistory.FirstOrDefault();
        StatusMessage = $"历史查询完成：{runs.Count:N0} 条。";
    }

    private void RerunSelectedHistory()
    {
        var run = SelectedRun?.Run;
        if (run is null) return;
        _currentTaskId = run.TaskId;
        _currentTaskCreatedAtUtc = null;
        TaskName = run.TaskName;
        ApplyRequest(run.Request);
        StatusMessage = "已加载历史任务配置。请重新生成预览并确认后执行。";
    }

    private static void OpenDataDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyTools");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { directory }, UseShellExecute = false });
    }

    private async Task RefreshQuarantineAsync()
    {
        var entries = await _quarantineWorkflow.GetEntriesAsync();
        QuarantineEntries.Clear();
        foreach (var entry in entries)
        {
            QuarantineEntries.Add(new QuarantineEntryViewModel(entry));
        }

        SelectedQuarantineEntry = QuarantineEntries.FirstOrDefault();
    }

    private async Task RestoreSelectedQuarantineAsync()
    {
        var selected = SelectedQuarantineEntry?.Entry;
        if (selected is null)
        {
            return;
        }

        var overwrite = false;
        if (File.Exists(selected.OriginalPath))
        {
            var answer = MessageBox.Show(
                "原始位置已有同名文件。是否覆盖现有文件并恢复隔离版本？",
                "恢复冲突",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            overwrite = true;
        }

        await _quarantineWorkflow.RestoreAsync(selected, overwrite);
        await RefreshQuarantineAsync();
        StatusMessage = $"已恢复：{selected.RelativePath}";
    }

    private async Task SaveRunHistoryAsync(CopyPlan plan, CopyExecutionResult result)
    {
        var items = result.EffectiveItems
            .Select(item => new RunHistoryItem(
                item.RelativePath,
                item.Action,
                item.Result,
                item.Bytes,
                item.ErrorMessage,
                item.Attempts))
            .ToArray();
        var run = new RunHistoryEntry(
            Guid.NewGuid(),
            _currentTaskId,
            string.IsNullOrWhiteSpace(TaskName) ? "临时任务" : TaskName.Trim(),
            plan.Request,
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.Status,
            result.SuccessfulFiles,
            result.SkippedFiles,
            result.Failures.Count,
            result.CopiedBytes,
            items);
        var quarantineEntries = result.EffectiveQuarantinedFiles
            .Select(file => new QuarantineEntry(
                Guid.NewGuid(),
                run.Id,
                file.OriginalPath,
                file.QuarantinePath,
                file.RelativePath,
                file.Length,
                file.QuarantinedAtUtc,
                null))
            .ToArray();
        run = run with { QuarantineEntries = quarantineEntries };
        await _repository.SaveRunAsync(run);
    }

    private async Task NotifyTaskResultAsync(Guid taskId, string taskName, CopyExecutionResult result)
    {
        if (_taskResultNotifier is null || !NotificationsEnabled) return;
        var errors = await _taskResultNotifier.SendAsync(new TaskResultNotification(
            taskId, taskName, result.Status, result.CompletedAtUtc,
            result.SuccessfulFiles, result.Failures.Count, result.CopiedBytes));
        if (errors.Count > 0)
            StatusMessage += $" 通知失败：{string.Join("；", errors)}";
    }

    private CopyTaskRequest BuildRequest(Guid? synchronizationId = null)
    {
        var minimumBytes = ParseMegabytes(MinimumSizeMbText, "最小文件大小");
        var maximumBytes = ParseMegabytes(MaximumSizeMbText, "最大文件大小");
        if (minimumBytes is not null && maximumBytes is not null && minimumBytes > maximumBytes)
        {
            throw new CopyTaskValidationException("最小文件大小不能大于最大文件大小。");
        }

        var sourcePaths = ParseSourcePaths(SourcePathsText)
            .Select(ToConfiguredPath).ToArray();
        var targetDirectory = ToConfiguredPath(TargetDirectory);
        return new CopyTaskRequest(
            sourcePaths.FirstOrDefault() ?? ToConfiguredPath(SourceDirectory),
            targetDirectory,
            SelectedMode.Value,
            BuildFilters(),
            SelectedVerification.Value,
            new RetryPolicy(MaxAttempts, TimeSpan.FromSeconds(RetryDelaySeconds), TimeSpan.FromMinutes(AvailabilityWaitMinutes)),
            new DeleteSafetyPolicy(MaximumDeleteItems, MaximumDeletePercentage),
            SelectedMode.Value is CopyMode.OneWaySync or CopyMode.UpdateSync or CopyMode.TwoWaySync
                ? synchronizationId ?? _currentTaskId
                : null,
            SelectedExistingFilePolicy.Value,
            new PerformanceOptions(
                BufferSizeKb * 1024,
                SpeedLimitMbPerSecond == 0 ? null : SpeedLimitMbPerSecond * 1024L * 1024L,
                MaxParallelFiles,
                AutoTunePerformance),
            new VersioningPolicy(PreserveOverwrittenFiles,
                string.IsNullOrWhiteSpace(VersionsDirectory) ? null : VersionsDirectory,
                VersionKeepLatest, TimeSpan.FromDays(VersionKeepDays)),
            new FileSecurityOptions(PreserveNtfsPermissions),
            VerificationProviderId: SelectedPluginVerification.Id,
            SourcePaths: sourcePaths,
            ConflictResolutionPolicy: SelectedConflictResolution.Value);
    }

    private string ToConfiguredPath(string path)
    {
        if (!UseStableVolumePaths || string.IsNullOrWhiteSpace(path) || path.Contains('{')) return path;
        try { return VolumeIdentifierResolver.ToStablePath(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException) { return path; }
    }

    private void ApplyRequest(CopyTaskRequest request)
    {
        SourcePathsText = string.Join(Environment.NewLine, request.EffectiveSourcePaths);
        UseStableVolumePaths = request.EffectiveSourcePaths.Append(request.TargetDirectory)
            .Any(path => path.StartsWith("\\\\?\\Volume{", StringComparison.OrdinalIgnoreCase));
        TargetDirectory = request.TargetDirectory;
        SelectedMode = Modes.First(mode => mode.Value == request.Mode);
        SelectedVerification = VerificationOptions.First(option => option.Value == request.VerificationMode);
        SelectedExistingFilePolicy = ExistingFilePolicies.First(option => option.Value == request.ExistingFilePolicy);
        IncludePatternsText = JoinPatterns(request.EffectiveFilters.EffectiveIncludePatterns);
        ExcludePatternsText = JoinPatterns(request.EffectiveFilters.EffectiveExcludePatterns);
        ExcludedDirectoriesText = JoinPatterns(request.EffectiveFilters.EffectiveExcludedDirectories);
        MinimumSizeMbText = FormatMegabytes(request.EffectiveFilters.MinimumBytes);
        MaximumSizeMbText = FormatMegabytes(request.EffectiveFilters.MaximumBytes);
        ModifiedAfterText = FormatLocalDate(request.EffectiveFilters.ModifiedAfterUtc);
        ModifiedBeforeText = FormatLocalDate(request.EffectiveFilters.ModifiedBeforeUtc);
        IncludeHiddenFiles = request.EffectiveFilters.IncludeHiddenFiles;
        IncludeSystemFiles = request.EffectiveFilters.IncludeSystemFiles;
        SelectedReparsePoint = ReparsePointOptions.First(option => option.Value == request.EffectiveFilters.ReparsePointPolicy);
        SelectedConflictResolution = ConflictResolutionOptions.First(option => option.Value == request.ConflictResolutionPolicy);
        MaxAttempts = request.EffectiveRetryPolicy.MaxAttempts;
        RetryDelaySeconds = (int)Math.Round(request.EffectiveRetryPolicy.Delay.TotalSeconds);
        AvailabilityWaitMinutes = (int)Math.Round((request.EffectiveRetryPolicy.AvailabilityWaitTimeout ?? TimeSpan.Zero).TotalMinutes);
        MaximumDeleteItems = request.EffectiveDeleteSafetyPolicy.MaximumItems;
        MaximumDeletePercentage = request.EffectiveDeleteSafetyPolicy.MaximumPercentage;
        BufferSizeKb = request.EffectivePerformance.BufferSizeBytes / 1024;
        SpeedLimitMbPerSecond = request.EffectivePerformance.SpeedLimitBytesPerSecond is null
            ? 0
            : (int)(request.EffectivePerformance.SpeedLimitBytesPerSecond.Value / 1024 / 1024);
        MaxParallelFiles = request.EffectivePerformance.MaxParallelFiles;
        AutoTunePerformance = request.EffectivePerformance.AutoTune;
        PreserveOverwrittenFiles = request.EffectiveVersioning.PreserveOverwrittenFiles;
        VersionsDirectory = request.EffectiveVersioning.VersionsDirectory ?? string.Empty;
        VersionKeepLatest = request.EffectiveVersioning.KeepLatestPerFile;
        VersionKeepDays = (int)Math.Round((request.EffectiveVersioning.KeepFor ?? TimeSpan.FromDays(30)).TotalDays);
        PreserveNtfsPermissions = request.EffectiveSecurity.PreserveNtfsPermissions;
        SelectedPluginVerification = PluginVerificationOptions.FirstOrDefault(option =>
            string.Equals(option.Id, request.VerificationProviderId, StringComparison.OrdinalIgnoreCase))
            ?? PluginVerificationOptions[0];
        InvalidatePlan();
    }

    private bool ResolveInteractiveConflicts()
    {
        while (CurrentPlan is { UnresolvedConflicts: > 0 } plan)
        {
            var source = plan.Items.First(item => item.Action == CopyAction.ConflictNeedsDecision
                && item.ConflictOrigin == ConflictOrigin.Source);
            var target = plan.Items.Single(item => item.Action == CopyAction.ConflictNeedsDecision
                && item.ConflictOrigin == ConflictOrigin.Target
                && string.Equals(item.RelativePath, source.RelativePath, StringComparison.OrdinalIgnoreCase));
            var dialog = new ConflictResolutionDialog(source, target)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            if (dialog.ShowDialog() != true || dialog.Resolution is null) return false;
            CurrentPlan = PlanEditor.ResolveConflict(plan, source.RelativePath, dialog.Resolution.Value);
        }
        return true;
    }

    private void BrowseVersionsDirectory()
    {
        var selected = _folderPicker.PickFolder("选择覆盖前版本保存目录", VersionsDirectory);
        if (selected is not null) { VersionsDirectory = selected; RefreshVersions(); }
    }

    private void RefreshVersions()
    {
        FileVersions.Clear();
        if (!string.IsNullOrWhiteSpace(VersionsDirectory))
            foreach (var entry in new FileVersionService().GetVersions(VersionsDirectory))
                FileVersions.Add(new FileVersionViewModel(entry));
        SelectedFileVersion = FileVersions.FirstOrDefault();
    }

    private void RestoreVersion()
    {
        var version = SelectedFileVersion?.Entry;
        if (version is null) return;
        var target = version.OriginalTargetPath;
        if (string.IsNullOrWhiteSpace(target))
        {
            var dialog = new SaveFileDialog { Title = "恢复覆盖前版本到…", Filter = "所有文件 (*.*)|*.*", FileName = "restored-version" };
            if (dialog.ShowDialog() != true) return;
            target = dialog.FileName;
        }
        if (File.Exists(target) && MessageBox.Show($"将用所选历史版本覆盖：\n{target}\n\n是否继续？", "版本恢复",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        new FileVersionService().Restore(version.VersionPath, target, overwrite: true);
        StatusMessage = $"覆盖前版本已恢复到：{target}";
    }

    private bool FilterPlanItem(object item)
    {
        if (item is not PlanItemViewModel viewModel || SelectedPlanFilter == "全部") return true;
        return SelectedPlanFilter switch
        {
            "新增" => viewModel.Item.Action == CopyAction.CopyNew,
            "覆盖" => viewModel.Item.Action == CopyAction.Overwrite,
            "跳过" => viewModel.Item.Action is CopyAction.SkipExisting or CopyAction.SkipUnchanged,
            "删除/隔离" => viewModel.Item.Action is CopyAction.QuarantineTarget or CopyAction.QuarantineSource
                or CopyAction.QuarantineDirectory or CopyAction.RemoveSource,
            "冲突" => viewModel.Item.Action is CopyAction.ConflictCopy or CopyAction.ConflictNeedsDecision,
            "校验" => viewModel.Item.Action == CopyAction.Verify,
            "目录" => viewModel.Item.IsDirectory,
            _ => true,
        };
    }

    private void UpdateProgress(CopyProgress progress)
    {
        ProgressPercent = progress.TotalBytes > 0
            ? Math.Clamp(progress.CopiedBytes * 100d / progress.TotalBytes, 0, 100)
            : progress.TotalFiles > 0
                ? Math.Clamp(progress.CompletedFiles * 100d / progress.TotalFiles, 0, 100)
                : 0;
        CurrentFileProgressPercent = progress.CurrentFileTotalBytes > 0
            ? Math.Clamp(progress.CurrentFileBytes * 100d / progress.CurrentFileTotalBytes, 0, 100)
            : 0;
        var elapsed = DateTime.UtcNow - _progressStartedAtUtc;
        var bytesPerSecond = elapsed.TotalSeconds > 0 ? progress.CopiedBytes / elapsed.TotalSeconds : 0;
        var sampleElapsed = DateTime.UtcNow - _lastProgressSampleAtUtc;
        if (sampleElapsed.TotalMilliseconds >= 250)
        {
            var instant = Math.Max(0, progress.CopiedBytes - _lastProgressSampleBytes) / sampleElapsed.TotalSeconds;
            _currentBytesPerSecond = _currentBytesPerSecond <= 0 ? instant : _currentBytesPerSecond * 0.65 + instant * 0.35;
            _lastProgressSampleAtUtc = DateTime.UtcNow;
            _lastProgressSampleBytes = progress.CopiedBytes;
        }
        var remaining = bytesPerSecond > 0 && progress.TotalBytes > progress.CopiedBytes
            ? TimeSpan.FromSeconds((progress.TotalBytes - progress.CopiedBytes) / bytesPerSecond)
            : TimeSpan.Zero;
        TransferStatistics = bytesPerSecond > 0
            ? $"当前 {FormatBytes((long)_currentBytesPerSecond)}/秒；平均 {FormatBytes((long)bytesPerSecond)}/秒；预计剩余 {FormatDuration(remaining)}；"
                + $"已完成 {progress.CompletedFiles:N0}/{progress.TotalFiles:N0}"
            : $"已完成 {progress.CompletedFiles:N0}/{progress.TotalFiles:N0}";
        StatusMessage = string.IsNullOrEmpty(progress.CurrentRelativePath)
            ? progress.Status
            : $"{progress.Status}：{progress.CurrentRelativePath}";
    }

    private void Cancel() => _operationCancellation?.Cancel();

    private void Pause()
    {
        _executionControl?.Pause();
        StatusMessage = "任务已暂停；当前临时文件保持不可见，继续后从当前块恢复。";
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    private void Resume()
    {
        _executionControl?.Resume();
        StatusMessage = "任务已继续。";
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    private void BeginOperation(string status)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        CanCancel = true;
        ProgressPercent = 0;
        CurrentFileProgressPercent = 0;
        TransferStatistics = string.Empty;
        _progressStartedAtUtc = DateTime.UtcNow;
        _lastProgressSampleAtUtc = _progressStartedAtUtc;
        _lastProgressSampleBytes = 0;
        _currentBytesPerSecond = 0;
        StatusMessage = status;
    }

    private void EndOperation()
    {
        CanCancel = false;
        IsBusy = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    private void InvalidatePlan()
    {
        CurrentPlan = null;
        PlanItems.Clear();
        StatusMessage = "配置已变化，请重新生成预览。";
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        BrowseSourceCommand.NotifyCanExecuteChanged();
        AddSourceFilesCommand.NotifyCanExecuteChanged();
        ClearSourcePathsCommand.NotifyCanExecuteChanged();
        BrowseTargetCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        SaveTaskCommand.NotifyCanExecuteChanged();
        LoadTaskCommand.NotifyCanExecuteChanged();
        DeleteTaskCommand.NotifyCanExecuteChanged();
        DuplicateTaskCommand.NotifyCanExecuteChanged();
        ExportTasksCommand.NotifyCanExecuteChanged();
        ImportTasksCommand.NotifyCanExecuteChanged();
        ExportRunLogCommand.NotifyCanExecuteChanged();
        BrowseBackupSourceCommand.NotifyCanExecuteChanged();
        BrowseBackupRepositoryCommand.NotifyCanExecuteChanged();
        BrowseBackupRestoreCommand.NotifyCanExecuteChanged();
        CreateBackupCommand.NotifyCanExecuteChanged();
        RefreshBackupSnapshotsCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
        CleanupBackupsCommand.NotifyCanExecuteChanged();
        SaveSettingsCommand.NotifyCanExecuteChanged();
        ResetSettingsCommand.NotifyCanExecuteChanged();
        ExportConfigurationCommand.NotifyCanExecuteChanged();
        RestoreConfigurationCommand.NotifyCanExecuteChanged();
        CheckUpdateCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        BrowseVersionsDirectoryCommand.NotifyCanExecuteChanged();
        RefreshVersionsCommand.NotifyCanExecuteChanged();
        RestoreVersionCommand.NotifyCanExecuteChanged();
        SaveAutoTriggerCommand.NotifyCanExecuteChanged();
        DeleteAutoTriggerCommand.NotifyCanExecuteChanged();
        AddStorageConnectionCommand.NotifyCanExecuteChanged();
        EditStorageConnectionCommand.NotifyCanExecuteChanged();
        DeleteStorageConnectionCommand.NotifyCanExecuteChanged();
        TestStorageConnectionCommand.NotifyCanExecuteChanged();
        PreviewStorageTransferCommand.NotifyCanExecuteChanged();
        ExecuteStorageTransferCommand.NotifyCanExecuteChanged();
        CreateSecureArchiveCommand.NotifyCanExecuteChanged();
        RestoreSecureArchiveCommand.NotifyCanExecuteChanged();
        RefreshIncompleteCommand.NotifyCanExecuteChanged();
        ResumeIncompleteCommand.NotifyCanExecuteChanged();
        SaveOrchestrationCommand.NotifyCanExecuteChanged();
        RunTaskQueueCommand.NotifyCanExecuteChanged();
        AddNotificationChannelCommand.NotifyCanExecuteChanged();
        EditNotificationChannelCommand.NotifyCanExecuteChanged();
        DeleteNotificationChannelCommand.NotifyCanExecuteChanged();
        TestNotificationChannelCommand.NotifyCanExecuteChanged();
        AddCurrentPathsToFavoritesCommand.NotifyCanExecuteChanged();
        UseRecentAsSourceCommand.NotifyCanExecuteChanged();
        UseFavoriteAsSourceCommand.NotifyCanExecuteChanged();
        UseFavoriteAsTargetCommand.NotifyCanExecuteChanged();
        RemoveFavoriteCommand.NotifyCanExecuteChanged();
        SaveFilterTemplateCommand.NotifyCanExecuteChanged();
        ApplyFilterTemplateCommand.NotifyCanExecuteChanged();
        DeleteFilterTemplateCommand.NotifyCanExecuteChanged();
        SaveTaskTemplateCommand.NotifyCanExecuteChanged();
        ApplyTaskTemplateCommand.NotifyCanExecuteChanged();
        DeleteTaskTemplateCommand.NotifyCanExecuteChanged();
        BrowseDuplicateDirectoryCommand.NotifyCanExecuteChanged();
        ScanDuplicatesCommand.NotifyCanExecuteChanged();
        QueryHistoryCommand.NotifyCanExecuteChanged();
        RerunSelectedHistoryCommand.NotifyCanExecuteChanged();
        OpenDataDirectoryCommand.NotifyCanExecuteChanged();
        RefreshHistoryCommand.NotifyCanExecuteChanged();
        RefreshQuarantineCommand.NotifyCanExecuteChanged();
        RestoreQuarantineCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        RetryFailuresCommand.NotifyCanExecuteChanged();
        ExcludeSelectedPlanItemsCommand.NotifyCanExecuteChanged();
        SaveScheduleCommand.NotifyCanExecuteChanged();
        DeleteScheduleCommand.NotifyCanExecuteChanged();
        RunScheduleNowCommand.NotifyCanExecuteChanged();
        RefreshSchedulesCommand.NotifyCanExecuteChanged();
    }

    private void HandleUnexpectedException(Exception exception)
    {
        StatusMessage = "操作失败，请检查路径、权限和磁盘状态。";
        MessageBox.Show(exception.Message, "CopyTools 错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowWarning(string message)
    {
        StatusMessage = message;
        MessageBox.Show(message, "无法创建任务", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static IReadOnlyList<string> SplitPatterns(string value) =>
        value.Split([';', ',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<string> ParseSourcePaths(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string JoinPatterns(IReadOnlyList<string> patterns) => string.Join("; ", patterns);

    private static long? ParseMegabytes(string value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var megabytes)
            || megabytes < 0
            || megabytes > long.MaxValue / 1024d / 1024d)
        {
            throw new CopyTaskValidationException($"{displayName}必须是有效的非负数。");
        }

        return (long)Math.Round(megabytes * 1024d * 1024d);
    }

    private static string FormatMegabytes(long? bytes) => bytes is null
        ? string.Empty
        : (bytes.Value / 1024d / 1024d).ToString("0.###", CultureInfo.CurrentCulture);

    private static DateTime? ParseLocalDate(string value, string displayName, bool endOfDay = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            throw new CopyTaskValidationException($"{displayName}格式无效，请使用 yyyy-MM-dd。");
        }

        if (endOfDay && local.TimeOfDay == TimeSpan.Zero)
        {
            local = local.Date.AddDays(1).AddTicks(-1);
        }

        return local.ToUniversalTime();
    }

    private static string FormatLocalDate(DateTime? utc) => utc is null
        ? string.Empty
        : utc.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}时{duration.Minutes}分"
        : duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}分{duration.Seconds}秒"
            : $"{Math.Max(0, duration.Seconds)}秒";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
