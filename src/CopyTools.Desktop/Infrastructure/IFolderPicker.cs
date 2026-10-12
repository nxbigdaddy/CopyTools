namespace CopyTools.Desktop.Infrastructure;

public interface IFolderPicker
{
    string? PickFolder(string title, string? initialDirectory = null);
}
