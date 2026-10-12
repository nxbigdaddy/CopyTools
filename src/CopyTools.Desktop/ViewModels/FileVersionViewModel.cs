using CopyTools.Engine;

namespace CopyTools.Desktop.ViewModels;

public sealed class FileVersionViewModel
{
    public FileVersionViewModel(FileVersionEntry entry) => Entry = entry;
    public FileVersionEntry Entry { get; }
    public string CreatedAt => Entry.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Size => Entry.Length >= 1024 * 1024 ? $"{Entry.Length / 1024d / 1024d:0.##} MB" : $"{Entry.Length / 1024d:0.##} KB";
    public string VersionPath => Entry.VersionPath;
    public string RelativePath => Entry.RelativePath ?? "旧版记录（原路径未知）";
    public string OriginalTargetPath => Entry.OriginalTargetPath ?? string.Empty;
}
