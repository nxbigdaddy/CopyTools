using System.Windows;
using CopyTools.Domain;

namespace CopyTools.Desktop;

public partial class ConflictResolutionDialog : Window
{
    public ConflictResolutionDialog(CopyPlanItem source, CopyPlanItem target)
    {
        InitializeComponent();
        DataContext = new
        {
            source.RelativePath,
            SourcePath = source.SourcePath,
            TargetPath = target.SourcePath,
            SourceDetails = $"{Format(source.Length)} · {source.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
            TargetDetails = $"{Format(target.Length)} · {target.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
        };
    }

    public ConflictResolutionPolicy? Resolution { get; private set; }
    private void Source_Click(object sender, RoutedEventArgs e) => Complete(ConflictResolutionPolicy.SourceWins);
    private void Target_Click(object sender, RoutedEventArgs e) => Complete(ConflictResolutionPolicy.TargetWins);
    private void Both_Click(object sender, RoutedEventArgs e) => Complete(ConflictResolutionPolicy.KeepBoth);
    private void Complete(ConflictResolutionPolicy value) { Resolution = value; DialogResult = true; }
    private static string Format(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.##} MB" : $"{bytes / 1024d:0.##} KB";
}
