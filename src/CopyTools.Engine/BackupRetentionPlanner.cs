using System.Globalization;
using CopyTools.Domain;

namespace CopyTools.Engine;

/// <summary>按 GFS 策略计算需要保留的快照集合，纯函数便于测试。</summary>
public static class BackupRetentionPlanner
{
    public static IReadOnlySet<Guid> SelectRetained(
        IReadOnlyList<BackupSnapshot> snapshots,
        BackupRetentionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var ordered = snapshots.OrderByDescending(snapshot => snapshot.CreatedAtUtc).ToList();
        var retained = new HashSet<Guid>();

        foreach (var snapshot in ordered.Take(Math.Max(0, policy.KeepLatest)))
        {
            retained.Add(snapshot.Id);
        }

        AddBuckets(ordered, policy.KeepDaily, snapshot => snapshot.CreatedAtUtc.Date, retained);
        AddBuckets(ordered, policy.KeepWeekly, snapshot =>
            (ISOWeek.GetYear(snapshot.CreatedAtUtc), ISOWeek.GetWeekOfYear(snapshot.CreatedAtUtc)), retained);
        AddBuckets(ordered, policy.KeepMonthly, snapshot =>
            (snapshot.CreatedAtUtc.Year, snapshot.CreatedAtUtc.Month), retained);
        AddBuckets(ordered, policy.KeepYearly, snapshot => snapshot.CreatedAtUtc.Year, retained);
        return retained;
    }

    private static void AddBuckets<TKey>(
        IReadOnlyList<BackupSnapshot> ordered,
        int count,
        Func<BackupSnapshot, TKey> keySelector,
        ISet<Guid> retained)
    {
        if (count <= 0)
        {
            return;
        }

        var seen = new HashSet<TKey>();
        foreach (var snapshot in ordered)
        {
            if (seen.Count >= count)
            {
                break;
            }

            if (seen.Add(keySelector(snapshot)))
            {
                retained.Add(snapshot.Id);
            }
        }
    }
}
