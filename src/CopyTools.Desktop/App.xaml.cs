using System.IO;
using System.Windows;
using CopyTools.Application;
using CopyTools.Desktop.Infrastructure;
using CopyTools.Desktop.ViewModels;
using CopyTools.Infrastructure;
using CopyTools.Engine;
using System.Windows.Interop;

namespace CopyTools.Desktop;

public partial class App : System.Windows.Application
{
    private TaskSchedulerService? _schedulerService;
    private AutoTriggerService? _autoTriggerService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // WPF 的硬件合成在部分远程桌面、虚拟显卡和混合显卡驱动下会出现白色客户区。
        // CopyTools 的界面不依赖 GPU 动画，软件合成能提供更稳定的首帧和 DPI 切换表现。
        System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopyTools");
        var repository = new SqliteCopyToolsRepository(Path.Combine(dataDirectory, "copytools.db"));
        var settingsStore = new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
        var credentialStore = new WindowsCredentialStore(Path.Combine(dataDirectory, "credentials"));
        await repository.InitializeAsync();
        var settings = await settingsStore.LoadAsync();
        ThemeManager.Apply(settings.Theme);
        var networkClients = new NetworkHttpClientFactory(settings, credentialStore);
        var pluginCatalog = PluginCatalog.Load(Path.Combine(dataDirectory, "plugins"));
        var workflow = new CopyWorkflow(
            new CopyPlanService(snapshotStore: repository),
            new CopyExecutor(verificationProviders: pluginCatalog.VerificationProviders));
        var notificationService = new TaskNotificationService(repository, credentialStore, networkClients.Create());
        var notificationChannels = new NotificationChannelService(repository, credentialStore, notificationService);
        var scheduledTaskRunner = new ScheduledTaskRunner(repository, workflow, notificationService, settingsStore);

        var viewModel = new MainViewModel(
            workflow,
            new WindowsFolderPicker(),
            repository,
            new QuarantineWorkflow(repository),
            scheduledTaskRunner,
            settingsStore,
            new StorageConnectionService(repository, credentialStore, pluginCatalog.StorageFactories, networkClients),
            notificationService,
            notificationChannels,
            pluginCatalog,
            credentialStore,
            networkClients);
        var window = new MainWindow(viewModel, settings);
        window.Show();
        await viewModel.InitializeAsync();
        _schedulerService = new TaskSchedulerService(repository, scheduledTaskRunner);
        _schedulerService.RunCompleted += (_, result) =>
            Dispatcher.Invoke(() => window.ShowScheduledResult(result));
        await _schedulerService.StartAsync();
        var autoTriggerService = new AutoTriggerService(repository, scheduledTaskRunner);
        _autoTriggerService = autoTriggerService;
        autoTriggerService.RunCompleted += (_, result) =>
            Dispatcher.Invoke(() => window.ShowScheduledResult(result));
        viewModel.SetAutoTriggerReload(async () =>
            await autoTriggerService.ConfigureAsync(await repository.GetAutoTriggerRulesAsync()));
        await autoTriggerService.ConfigureAsync(await repository.GetAutoTriggerRulesAsync());
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_schedulerService is not null)
        {
            await _schedulerService.DisposeAsync();
        }


        if (_autoTriggerService is not null)
        {
            await _autoTriggerService.DisposeAsync();
        }

        base.OnExit(e);
    }
}
