namespace CopyTools.Domain;

/// <summary>祖父-父-子（GFS）保留策略：按日、周、月、年分层保留最近快照。</summary>
public sealed record BackupRetentionPolicy(
    int KeepLatest = 1,
    int KeepDaily = 0,
    int KeepWeekly = 0,
    int KeepMonthly = 0,
    int KeepYearly = 0)
{
    public static BackupRetentionPolicy Default { get; } = new(KeepLatest: 1);

    public int TotalKeepTarget => KeepLatest + KeepDaily + KeepWeekly + KeepMonthly + KeepYearly;
}
