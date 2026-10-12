namespace CopyTools.Application;

/// <summary>轻量本地化服务：内置 zh-CN 与 en-US 资源，缺失键回退中文，再回退键名。</summary>
public sealed class LocalizationService
{
    public static readonly string[] SupportedLanguages = ["zh-CN", "en-US"];

    private const string FallbackLanguage = "zh-CN";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Resources =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app.name"] = "CopyTools 文件复制与同步",
                ["nav.newCopyTask"] = "新建复制任务",
                ["nav.newSyncTask"] = "新建同步任务",
                ["nav.dashboard"] = "仪表盘",
                ["nav.globalSearch"] = "全局搜索",
                ["nav.notifications"] = "通知中心",
                ["common.confirm"] = "确认",
                ["common.cancel"] = "取消",
                ["common.preview"] = "生成预览",
                ["common.execute"] = "开始执行",
                ["security.hint"] = "先预览，再复制；删除进入隔离区",
                ["result.success"] = "成功",
                ["result.partial"] = "部分成功",
                ["result.failed"] = "失败",
            },
            ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["app.name"] = "CopyTools File Copy & Sync",
                ["nav.newCopyTask"] = "New Copy Task",
                ["nav.newSyncTask"] = "New Sync Task",
                ["nav.dashboard"] = "Dashboard",
                ["nav.globalSearch"] = "Search",
                ["nav.notifications"] = "Notifications",
                ["common.confirm"] = "Confirm",
                ["common.cancel"] = "Cancel",
                ["common.preview"] = "Preview",
                ["common.execute"] = "Run",
                ["security.hint"] = "Preview first, copy second; deletions go to quarantine",
                ["result.success"] = "Success",
                ["result.partial"] = "Partial success",
                ["result.failed"] = "Failed",
            },
        };

    public string Get(string key, string? language = null)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? FallbackLanguage : language;
        if (Resources.TryGetValue(lang, out var table) && table.TryGetValue(key, out var value))
        {
            return value;
        }

        if (!string.Equals(lang, FallbackLanguage, StringComparison.OrdinalIgnoreCase)
            && Resources.TryGetValue(FallbackLanguage, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }
}
