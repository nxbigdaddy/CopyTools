using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed record BackupSnapshotViewModel(BackupSnapshot Snapshot)
{
    public string CreatedAt => Snapshot.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string Mode => Snapshot.Mode == BackupMode.IncrementalHistory ? "历史增量" : "完全快照";

    public int Files => Snapshot.Files.Count;

    public string Size => FormatBytes(Snapshot.Files.Sum(file => file.Length));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
