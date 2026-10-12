using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CopyTools.Desktop.ViewModels;
using CopyTools.Domain;
using MessageBox = System.Windows.MessageBox;

namespace CopyTools.Desktop;

public partial class MainWindow : Window
{
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _trayTasksMenu;
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel, ApplicationSettings? settings = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }
        };
        ContentRendered += (_, _) => RefreshVisualTree();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        _viewModel = viewModel;
        DataContext = viewModel;
        AllowDrop = true;
        PreviewDragOver += (_, args) =>
        {
            args.Effects = args.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            args.Handled = true;
        };
        Drop += (_, args) =>
        {
            if (args.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths) viewModel.AddSourcePaths(paths);
        };
        if (settings is { } saved)
        {
            Width = saved.WindowWidth; Height = saved.WindowHeight;
            if (saved.WindowLeft is { } left && saved.WindowTop is { } top)
            { Left = left; Top = top; WindowStartupLocation = WindowStartupLocation.Manual; }
            MainTabs.SelectedIndex = Math.Min(saved.SelectedTabIndex, Math.Max(0, MainTabs.Items.Count - 1));
            for (var index = 0; index < Math.Min(saved.EffectivePlanColumnWidths.Count, PlanGrid.Columns.Count); index++)
                PlanGrid.Columns[index].Width = new DataGridLength(saved.EffectivePlanColumnWidths[index]);
            if (saved.WindowMaximized) WindowState = WindowState.Maximized;
        }
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "CopyTools 文件复制与同步",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(),
        };
        _trayIcon.ContextMenuStrip.Items.Add("显示 CopyTools", null, (_, _) => RestoreFromTray());
        _trayIcon.ContextMenuStrip.Items.Add("打开使用帮助 (F1)", null, (_, _) => OpenHelp());
        _trayTasksMenu = new System.Windows.Forms.ToolStripMenuItem("运行已保存任务");
        _trayIcon.ContextMenuStrip.Items.Add(_trayTasksMenu);
        _trayIcon.ContextMenuStrip.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) =>
        {
            _allowClose = true;
            Close();
        });
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        viewModel.SavedTasksChanged += ViewModelOnSavedTasksChanged;
        viewModel.TaskFinished += ViewModelOnTaskFinished;
        RefreshTrayTasks();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && viewModel.MinimizeToTray)
            {
                // 保留 WPF 合成目标，只从任务栏移除窗口。部分远程桌面/虚拟显卡在 Hide/Show 后
                // 无法可靠恢复软件渲染表面，会留下可交互但客户区空白的窗口。
                ShowInTaskbar = false;
                ShowTrayBalloon("CopyTools 仍在后台运行", "计划任务和实时触发不会中断。",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        };
        Closing += (_, args) =>
        {
            if (!_allowClose && viewModel.MinimizeToTray)
            {
                args.Cancel = true;
                ShowInTaskbar = false;
                WindowState = WindowState.Minimized;
            }
        };
        Closed += async (_, _) =>
        {
            var bounds = RestoreBounds;
            try { await viewModel.SaveWindowStateAsync(bounds.Width, bounds.Height, bounds.Left, bounds.Top,
                WindowState == WindowState.Maximized, MainTabs.SelectedIndex,
                PlanGrid.Columns.Select(column => column.ActualWidth).ToArray()); } catch (IOException) { }
            viewModel.SavedTasksChanged -= ViewModelOnSavedTasksChanged;
            viewModel.TaskFinished -= ViewModelOnTaskFinished;
            _trayIcon.Dispose();
        };
    }

    private void ViewModelOnSavedTasksChanged(object? sender, EventArgs e) => RefreshTrayTasks();

    private void ViewModelOnTaskFinished(object? sender, TaskResultNotification result) =>
        ShowTrayBalloon($"任务：{result.TaskName}",
            $"{result.Status}；成功 {result.SuccessfulFiles:N0}，失败 {result.FailedFiles:N0}。",
            result.FailedFiles == 0 ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning);

    private void RefreshTrayTasks()
    {
        _trayTasksMenu.DropDownItems.Clear();
        foreach (var task in _viewModel.SavedTasks.Take(15))
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(task.Name) { Tag = task.Task.Id };
            item.Click += async (_, _) =>
            {
                try
                {
                    var result = await _viewModel.RunSavedTaskFromTrayAsync((Guid)item.Tag!);
                    ShowScheduledResult(result);
                    if (result.Status == "成功")
                        ShowTrayBalloon($"任务：{result.TaskName}", "执行成功。", System.Windows.Forms.ToolTipIcon.Info);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.Message, "运行托盘任务", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            _trayTasksMenu.DropDownItems.Add(item);
        }
        if (_trayTasksMenu.DropDownItems.Count == 0)
            _trayTasksMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("暂无已保存任务") { Enabled = false });
    }

    private void ProxyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox) _viewModel.ProxyPassword = passwordBox.Password;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_OnClick(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void ToggleMaximizeRestore() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Help_OnClick(object sender, RoutedEventArgs e) => OpenHelp();

    private void HelpCommand_OnExecuted(object sender, ExecutedRoutedEventArgs e) => OpenHelp();

    private static void OpenHelp()
    {
        var helpPath = Path.Combine(AppContext.BaseDirectory, "help", "CopyTools.chm");
        if (!File.Exists(helpPath))
        {
            MessageBox.Show(
                $"未找到帮助文件：\n{helpPath}\n\n请重新安装 CopyTools，或确认 help 文件夹完整。",
                "CopyTools 帮助",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(helpPath) { UseShellExecute = true });
    }

    public void ShowScheduledResult(ScheduledRunResult result)
    {
        if (result.Status is "成功" or "已取消")
        {
            return;
        }

        MessageBox.Show(
            result.Message ?? $"计划任务“{result.TaskName}”执行结果：{result.Status}。",
            "CopyTools 计划任务",
            MessageBoxButton.OK,
            result.Status == "等待人工确认" ? MessageBoxImage.Warning : MessageBoxImage.Error);
        ShowTrayBalloon(
            $"计划任务：{result.TaskName}",
            result.Message ?? result.Status,
            result.Status == "等待人工确认"
                ? System.Windows.Forms.ToolTipIcon.Warning
                : System.Windows.Forms.ToolTipIcon.Error);
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
        RefreshVisualTree();
    }

    private void RefreshVisualTree() =>
        Dispatcher.BeginInvoke(() =>
        {
            InvalidateVisual();
            UpdateLayout();
        }, DispatcherPriority.Render);

    private void ShowTrayBalloon(
        string title,
        string text,
        System.Windows.Forms.ToolTipIcon icon)
    {
        if (DataContext is MainViewModel { NotificationsEnabled: true })
        {
            _trayIcon.ShowBalloonTip(5000, title, text, icon);
        }
    }
}
