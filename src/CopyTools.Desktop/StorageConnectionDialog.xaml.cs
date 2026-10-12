using System.Windows;
using System.Windows.Controls;
using System.IO;
using CopyTools.Domain;
using CopyTools.Infrastructure;
using CopyTools.Engine;
using MessageBox = System.Windows.MessageBox;

namespace CopyTools.Desktop;

public partial class StorageConnectionDialog : Window
{
    private readonly StorageConnectionProfile? _existing;

    public StorageConnectionDialog(
        StorageConnectionProfile? existing,
        IReadOnlyDictionary<string, IPluginStorageAdapterFactory>? pluginFactories = null)
    {
        InitializeComponent();
        _existing = existing;
        KindBox.ItemsSource = Enum.GetValues<StorageConnectionKind>()
            .Where(kind => kind != StorageConnectionKind.Plugin)
            .Select(kind => new KindOption(kind, GetKindName(kind)))
            .Concat((pluginFactories ?? new Dictionary<string, IPluginStorageAdapterFactory>()).Values
                .Select(factory => new KindOption(StorageConnectionKind.Plugin, $"插件：{factory.DisplayName}", factory.Id)))
            .ToArray();
        KindBox.SelectedItem = ((IEnumerable<KindOption>)KindBox.ItemsSource)
            .FirstOrDefault(option => option.Kind == (existing?.Kind ?? StorageConnectionKind.Local)
                && (option.Kind != StorageConnectionKind.Plugin || option.PluginId == existing?.Endpoint))
            ?? ((IEnumerable<KindOption>)KindBox.ItemsSource).First();
        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            EndpointBox.Text = existing.Endpoint;
            RootPathBox.Text = existing.RootPath;
            UserNameBox.Text = existing.UserName;
            RegionBox.Text = existing.Region;
            BucketBox.Text = existing.Bucket;
        }

        UpdateHelp();
    }

    public StorageConnectionProfile? Profile { get; private set; }
    public string? Secret => string.IsNullOrEmpty(SecretBox.Password) ? null : SecretBox.Password;

    private StorageConnectionKind SelectedKind => (KindBox.SelectedItem as KindOption)?.Kind
        ?? StorageConnectionKind.Local;

    private void KindBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHelp();

    private void UpdateHelp()
    {
        if (!IsInitialized)
        {
            return;
        }

        var kind = SelectedKind;
        var objectStorage = kind is StorageConnectionKind.S3 or StorageConnectionKind.AliyunOss or StorageConnectionKind.TencentCos;
        RegionBox.IsEnabled = objectStorage;
        BucketBox.IsEnabled = objectStorage;
        BrowseButton.Visibility = kind is StorageConnectionKind.Local or StorageConnectionKind.RemovableDrive
            ? Visibility.Visible : Visibility.Collapsed;
        EndpointBox.IsEnabled = kind != StorageConnectionKind.Plugin;
        if (kind == StorageConnectionKind.Plugin && KindBox.SelectedItem is KindOption pluginOption)
            EndpointBox.Text = pluginOption.PluginId ?? string.Empty;
        SecretHint.Text = _existing is null
            ? "不需要凭据时可留空；SFTP 此字段填写私钥文件路径。"
            : "留空将保留现有安全凭据；填写内容会覆盖。SFTP 此字段填写私钥文件路径。";
        HelpText.Text = kind switch
        {
            StorageConnectionKind.Local => "端点填写本地目录。",
            StorageConnectionKind.NetworkShare => "端点填写 UNC 路径，例如 \\\\server\\share。",
            StorageConnectionKind.RemovableDrive => "通过“选择”保存卷 GUID，盘符变化后仍可重新定位。",
            StorageConnectionKind.WebDav => "端点填写完整 HTTPS WebDAV URL。",
            StorageConnectionKind.Ftp or StorageConnectionKind.Ftps or StorageConnectionKind.Sftp => "端点填写主机或 URL，可包含端口。",
            StorageConnectionKind.OneDrive or StorageConnectionKind.Dropbox or StorageConnectionKind.BaiduNetdisk => "将 OAuth 访问令牌填入安全凭据框。",
            StorageConnectionKind.GoogleDrive => "安全凭据填写 OAuth 令牌；根目录可填写 Drive 文件夹 ID，默认 root。",
            StorageConnectionKind.Plugin => "该连接由已加载插件提供；端点为插件适配器 ID，由 CopyTools 自动维护。",
            _ when objectStorage => "填写 HTTPS 端点、区域、存储桶、Access Key 和 Secret Key。",
            _ => string.Empty,
        };
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = SelectedKind == StorageConnectionKind.RemovableDrive ? "选择可移动磁盘中的目录" : "选择本地目录",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        if (SelectedKind == StorageConnectionKind.RemovableDrive)
        {
            var mount = Path.GetPathRoot(Path.GetFullPath(dialog.SelectedPath))!;
            EndpointBox.Text = VolumeIdentifierResolver.GetStableIdentifier(dialog.SelectedPath);
            var relative = Path.GetRelativePath(mount, dialog.SelectedPath);
            RootPathBox.Text = relative == "." ? string.Empty : relative;
        }
        else
        {
            EndpointBox.Text = dialog.SelectedPath;
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedOption = KindBox.SelectedItem as KindOption;
        var endpoint = SelectedKind == StorageConnectionKind.Plugin ? selectedOption?.PluginId ?? string.Empty : EndpointBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(endpoint))
        {
            MessageBox.Show("请填写连接名称和端点/路径。", "存储连接", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var now = DateTime.UtcNow;
        var id = _existing?.Id ?? Guid.NewGuid();
        Profile = new StorageConnectionProfile(
            id, NameBox.Text.Trim(), SelectedKind, endpoint, RootPathBox.Text.Trim(),
            UserNameBox.Text.Trim(), _existing?.CredentialTarget ?? $"CopyTools/Storage/{id:D}",
            RegionBox.Text.Trim(), BucketBox.Text.Trim(), _existing?.CreatedAtUtc ?? now, now);
        DialogResult = true;
    }

    private sealed record KindOption(StorageConnectionKind Kind, string Name, string? PluginId = null);

    private static string GetKindName(StorageConnectionKind kind) => kind switch
    {
        StorageConnectionKind.Local => "本地目录",
        StorageConnectionKind.NetworkShare => "Windows 网络共享",
        StorageConnectionKind.RemovableDrive => "可移动磁盘",
        StorageConnectionKind.Mtp => "MTP 手机/相机",
        StorageConnectionKind.WebDav => "WebDAV",
        StorageConnectionKind.Ftp => "FTP",
        StorageConnectionKind.Ftps => "FTPS",
        StorageConnectionKind.Sftp => "SFTP",
        StorageConnectionKind.OneDrive => "OneDrive",
        StorageConnectionKind.GoogleDrive => "Google Drive",
        StorageConnectionKind.Dropbox => "Dropbox",
        StorageConnectionKind.S3 => "Amazon S3 / S3 兼容",
        StorageConnectionKind.AliyunOss => "阿里云 OSS",
        StorageConnectionKind.TencentCos => "腾讯云 COS",
        StorageConnectionKind.BaiduNetdisk => "百度网盘",
        _ => kind.ToString(),
    };
}
