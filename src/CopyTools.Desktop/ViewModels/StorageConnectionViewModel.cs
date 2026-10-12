using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class StorageConnectionViewModel(StorageConnectionProfile profile)
{
    public StorageConnectionProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Kind => Profile.Kind switch
    {
        StorageConnectionKind.Local => "本地目录",
        StorageConnectionKind.NetworkShare => "Windows 网络共享",
        StorageConnectionKind.RemovableDrive => "可移动磁盘（卷标识）",
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
        StorageConnectionKind.Plugin => "插件存储",
        _ => Profile.Kind.ToString(),
    };
    public string Endpoint => Profile.Endpoint;
    public string RootPath => Profile.RootPath;
    public string CredentialState => string.IsNullOrWhiteSpace(Profile.CredentialTarget) ? "不使用" : "Windows 安全存储";
}
