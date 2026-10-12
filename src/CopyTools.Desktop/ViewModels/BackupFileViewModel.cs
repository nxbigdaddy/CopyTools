using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class BackupFileViewModel : ObservableObject
{
    private bool _isSelected;

    public BackupFileViewModel(BackupFileRecord file)
    {
        File = file;
    }

    public BackupFileRecord File { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string RelativePath => File.RelativePath;

    public long Length => File.Length;

    public string ModifiedAt => File.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
