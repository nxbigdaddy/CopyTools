using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed record QuarantineEntryViewModel(QuarantineEntry Entry)
{
    public string RelativePath => Entry.RelativePath;

    public string OriginalPath => Entry.OriginalPath;

    public string QuarantinePath => Entry.QuarantinePath;

    public string Size => FormatBytes(Entry.Length);

    public string QuarantinedAt => Entry.QuarantinedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

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
