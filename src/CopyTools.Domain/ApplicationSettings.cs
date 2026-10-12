namespace CopyTools.Domain;

public sealed record ApplicationSettings(
    bool NotificationsEnabled = true,
    bool MinimizeToTray = true,
    bool StartWithWindows = false,
    string Theme = "Light",
    int LogRetentionDays = 180,
    string Language = "zh-CN",
    ExistingFilePolicy DefaultExistingFilePolicy = ExistingFilePolicy.ModeDefault,
    VerificationMode DefaultVerificationMode = VerificationMode.Quick,
    int DefaultRetryAttempts = 3,
    int DefaultRetryDelaySeconds = 1,
    int DefaultAvailabilityWaitMinutes = 2,
    int DefaultMaximumDeleteItems = 1000,
    double DefaultMaximumDeletePercentage = 25,
    int DefaultBufferSizeKb = 1024,
    int DefaultSpeedLimitMbPerSecond = 0,
    int DefaultMaxParallelFiles = 4,
    bool DefaultAutoTunePerformance = true,
    int BackgroundMaxParallelFiles = 1,
    int BackgroundSpeedLimitMbPerSecond = 64,
    bool UseSystemProxy = true,
    string ProxyUrl = "",
    string ProxyUserName = "",
    string ProxyCredentialTarget = "CopyTools/Proxy",
    IReadOnlyList<string>? RecentPaths = null,
    IReadOnlyList<string>? FavoritePaths = null,
    double WindowWidth = 1240,
    double WindowHeight = 820,
    double? WindowLeft = null,
    double? WindowTop = null,
    bool WindowMaximized = false,
    int SelectedTabIndex = 0,
    string UpdateManifestUrl = "",
    bool CheckUpdatesOnStartup = false,
    string PlanFilter = "全部",
    IReadOnlyList<double>? PlanColumnWidths = null,
    string LogLevel = "Information",
    string AiProvider = "rule",
    string AiEndpoint = "",
    string AiModel = "",
    string AiApiKeyCredentialTarget = "CopyTools/AiApiKey")
{
    public IReadOnlyList<string> EffectiveRecentPaths => RecentPaths ?? [];
    public IReadOnlyList<string> EffectiveFavoritePaths => FavoritePaths ?? [];
    public IReadOnlyList<double> EffectivePlanColumnWidths => PlanColumnWidths ?? [];

    public ApplicationSettings Normalize() => this with
    {
        Theme = Theme is "Light" or "Dark" or "System" ? Theme : "Light",
        Language = Language is "zh-CN" or "en-US" or "System" ? Language : "zh-CN",
        LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 3650),
        DefaultRetryAttempts = Math.Clamp(DefaultRetryAttempts, 1, 10),
        DefaultRetryDelaySeconds = Math.Clamp(DefaultRetryDelaySeconds, 0, 3600),
        DefaultAvailabilityWaitMinutes = Math.Clamp(DefaultAvailabilityWaitMinutes, 0, 1440),
        DefaultMaximumDeleteItems = Math.Clamp(DefaultMaximumDeleteItems, 1, 1_000_000),
        DefaultMaximumDeletePercentage = Math.Clamp(DefaultMaximumDeletePercentage, 0.1, 100),
        DefaultBufferSizeKb = Math.Clamp(DefaultBufferSizeKb, 64, 16 * 1024),
        DefaultSpeedLimitMbPerSecond = Math.Clamp(DefaultSpeedLimitMbPerSecond, 0, 10 * 1024),
        DefaultMaxParallelFiles = Math.Clamp(DefaultMaxParallelFiles, 1, 16),
        BackgroundMaxParallelFiles = Math.Clamp(BackgroundMaxParallelFiles, 1, 16),
        BackgroundSpeedLimitMbPerSecond = Math.Clamp(BackgroundSpeedLimitMbPerSecond, 0, 10 * 1024),
        ProxyUrl = ProxyUrl.Trim(),
        ProxyUserName = ProxyUserName.Trim(),
        RecentPaths = RecentPaths is null ? null : NormalizePaths(EffectiveRecentPaths, 20),
        FavoritePaths = FavoritePaths is null ? null : NormalizePaths(EffectiveFavoritePaths, 100),
        WindowWidth = Math.Clamp(double.IsFinite(WindowWidth) ? WindowWidth : 1240, 980, 7680),
        WindowHeight = Math.Clamp(double.IsFinite(WindowHeight) ? WindowHeight : 820, 680, 4320),
        WindowLeft = WindowLeft is { } left && double.IsFinite(left) ? left : null,
        WindowTop = WindowTop is { } top && double.IsFinite(top) ? top : null,
        SelectedTabIndex = Math.Clamp(SelectedTabIndex, 0, 50),
        UpdateManifestUrl = UpdateManifestUrl.Trim(),
        LogLevel = LogLevel is "Debug" or "Information" or "Warning" or "Error" ? LogLevel : "Information",
        PlanFilter = PlanFilter is "全部" or "新增" or "覆盖" or "跳过" or "删除/隔离" or "冲突" or "校验" or "目录"
            ? PlanFilter : "全部",
        PlanColumnWidths = PlanColumnWidths is null ? null : PlanColumnWidths
            .Where(width => double.IsFinite(width)).Select(width => Math.Clamp(width, 40, 3000)).Take(32).ToArray(),
        AiProvider = AiProvider is "rule" or "ollama" or "openai" ? AiProvider : "rule",
        AiEndpoint = AiEndpoint.Trim(),
        AiModel = AiModel.Trim(),
        AiApiKeyCredentialTarget = string.IsNullOrWhiteSpace(AiApiKeyCredentialTarget)
            ? "CopyTools/AiApiKey" : AiApiKeyCredentialTarget.Trim(),
    };

    private static IReadOnlyList<string> NormalizePaths(IEnumerable<string> paths, int limit) =>
        paths.Select(path => path.Trim()).Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
}
