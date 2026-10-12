namespace CopyTools.Domain;

public sealed record InstalledApplication(
    string DisplayName,
    string? Version,
    string? Publisher);

/// <summary>应用状态快照：机器信息与已安装应用清单，用于换机/重装后的环境还原参考。</summary>
public sealed record AppStateManifest(
    string MachineName,
    string OsDescription,
    string OsArchitecture,
    DateTime CapturedAtUtc,
    IReadOnlyList<InstalledApplication> InstalledApplications);
