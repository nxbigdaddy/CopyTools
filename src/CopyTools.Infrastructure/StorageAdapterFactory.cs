using System.Net;
using System.Runtime.Versioning;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class StorageAdapterFactory(
    WindowsCredentialStore credentialStore,
    IReadOnlyDictionary<string, IPluginStorageAdapterFactory>? pluginFactories = null,
    NetworkHttpClientFactory? networkClients = null)
{
    public IStorageAdapter Create(StorageConnectionProfile profile)
    {
        var credential = string.IsNullOrWhiteSpace(profile.CredentialTarget)
            ? null
            : credentialStore.Read(profile.CredentialTarget);
        var userName = string.IsNullOrWhiteSpace(profile.UserName) ? credential?.UserName ?? string.Empty : profile.UserName;
        var secret = credential?.Secret ?? string.Empty;

        return profile.Kind switch
        {
            StorageConnectionKind.Local => new LocalStorageAdapter(CombineLocal(profile.Endpoint, profile.RootPath)),
            StorageConnectionKind.NetworkShare => new NetworkShareStorageAdapter(
                CombineLocal(profile.Endpoint, profile.RootPath),
                credential is null ? null : new NetworkCredential(userName, secret)),
            StorageConnectionKind.RemovableDrive => new LocalStorageAdapter(Path.Combine(
                VolumeIdentifierResolver.ResolveMountPoint(profile.Endpoint), profile.RootPath)),
            StorageConnectionKind.Mtp => new MtpStorageAdapter(profile.Endpoint, profile.RootPath),
            StorageConnectionKind.WebDav => CreateHttp(client => new WebDavStorageAdapter(
                RequireUri(profile.Endpoint), client), networkClients,
                credential is null ? null : new NetworkCredential(userName, secret)),
            StorageConnectionKind.Ftp or StorageConnectionKind.Ftps => CreateFtp(profile, userName, secret),
            StorageConnectionKind.Sftp => CreateSftp(profile, userName, secret),
            StorageConnectionKind.OneDrive => CreateHttp(client => new OneDriveStorageAdapter(RequireSecret(secret), profile.RootPath, client), networkClients),
            StorageConnectionKind.GoogleDrive => CreateHttp(client => new GoogleDriveStorageAdapter(RequireSecret(secret), string.IsNullOrWhiteSpace(profile.RootPath) ? "root" : profile.RootPath, client), networkClients),
            StorageConnectionKind.Dropbox => CreateHttp(client => new DropboxStorageAdapter(RequireSecret(secret), profile.RootPath, client), networkClients),
            StorageConnectionKind.S3 => CreateS3(profile, userName, secret, "S3", networkClients),
            StorageConnectionKind.AliyunOss => CreateS3(profile, userName, secret, "阿里云 OSS（S3 兼容）", networkClients),
            StorageConnectionKind.TencentCos => CreateS3(profile, userName, secret, "腾讯云 COS（S3 兼容）", networkClients),
            StorageConnectionKind.BaiduNetdisk => CreateHttp(client => new BaiduNetdiskStorageAdapter(RequireSecret(secret), profile.RootPath, client), networkClients),
            StorageConnectionKind.Minio => CreateS3(profile, userName, secret, "MinIO（S3 兼容）", networkClients),
            StorageConnectionKind.BackblazeB2 => CreateS3(profile, userName, secret, "Backblaze B2（S3 兼容）", networkClients),
            StorageConnectionKind.Jianguoyun => CreateHttp(client => new WebDavStorageAdapter(
                new Uri(string.IsNullOrWhiteSpace(profile.Endpoint)
                    ? "https://dav.jianguoyun.com/dav/"
                    : profile.Endpoint), client), networkClients,
                credential is null ? null : new NetworkCredential(userName, secret)),
            StorageConnectionKind.AzureBlob => CreateHttp(client => new AzureBlobStorageAdapter(
                RequireEndpoint(profile), userName, RequireSecret(secret), Container(profile), profile.RootPath, client), networkClients),
            StorageConnectionKind.AliyunDrive or StorageConnectionKind.QuarkNetdisk
                or StorageConnectionKind.TianyiNetdisk or StorageConnectionKind.Y123Netdisk =>
                CreateGenericCloud(profile, secret, networkClients),
            StorageConnectionKind.Plugin => CreatePlugin(profile, userName, secret, pluginFactories),
            _ => throw new NotSupportedException($"尚不支持存储类型 {profile.Kind}。"),
        };
    }

    private static IStorageAdapter CreateGenericCloud(
        StorageConnectionProfile profile,
        string secret,
        NetworkHttpClientFactory? networkClients)
    {
        var endpoint = string.IsNullOrWhiteSpace(profile.Endpoint)
            ? DefaultCloudEndpoint(profile.Kind)
            : profile.Endpoint;
        return CreateHttp(client => new GenericJsonCloudStorageAdapter(
            endpoint, RequireSecret(secret), profile.RootPath, client), networkClients);
    }

    private static string DefaultCloudEndpoint(StorageConnectionKind kind) => kind switch
    {
        StorageConnectionKind.AliyunDrive => "https://openapi.alipan.com/",
        StorageConnectionKind.QuarkNetdisk => "https://pan.quark.cn/",
        StorageConnectionKind.TianyiNetdisk => "https://cloud.189.cn/",
        StorageConnectionKind.Y123Netdisk => "https://www.123pan.com/",
        _ => throw new NotSupportedException($"尚不支持存储类型 {kind}。"),
    };

    private static string RequireEndpoint(StorageConnectionProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.Endpoint)
            ? profile.Endpoint
            : throw new InvalidOperationException("该连接缺少服务端点。");

    private static string Container(StorageConnectionProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Bucket) ? "root" : profile.Bucket;

    private static IStorageAdapter CreatePlugin(
        StorageConnectionProfile profile,
        string userName,
        string secret,
        IReadOnlyDictionary<string, IPluginStorageAdapterFactory>? factories)
    {
        if (factories is null || !factories.TryGetValue(profile.Endpoint, out var factory))
            throw new InvalidOperationException($"插件存储适配器未加载：{profile.Endpoint}。");
        return factory.Create(profile, userName, secret);
    }

    private static IStorageAdapter CreateFtp(StorageConnectionProfile profile, string userName, string secret)
    {
        var uri = ParseServer(profile.Endpoint, profile.Kind == StorageConnectionKind.Ftps ? 990 : 21);
        return new FtpStorageAdapter(
            uri.Host, uri.Port, new NetworkCredential(userName, RequireSecret(secret)),
            profile.Kind == StorageConnectionKind.Ftps, profile.RootPath);
    }

    private static IStorageAdapter CreateSftp(StorageConnectionProfile profile, string userName, string secret)
    {
        var uri = ParseServer(profile.Endpoint, 22);
        return new SftpStorageAdapter(
            uri.Host, uri.Port, userName, profile.RootPath,
            string.IsNullOrWhiteSpace(secret) ? null : secret);
    }

    private static IStorageAdapter CreateS3(
        StorageConnectionProfile profile,
        string accessKey,
        string secret,
        string displayName,
        NetworkHttpClientFactory? networkClients)
    {
        var client = networkClients?.Create();
        var adapter = new S3StorageAdapter(
            profile.Endpoint, profile.Region, profile.Bucket,
            accessKey, RequireSecret(secret), profile.RootPath, client: client);
        IStorageAdapter named = displayName == "S3" ? adapter : new NamedStorageAdapter(displayName, adapter);
        return client is null ? named : new OwnedHttpStorageAdapter(named, client);
    }

    private static IStorageAdapter CreateHttp(
        Func<HttpClient, IStorageAdapter> create,
        NetworkHttpClientFactory? networkClients,
        NetworkCredential? serverCredential = null)
    {
        if (networkClients is null)
        {
            var fallback = new HttpClient();
            return new OwnedHttpStorageAdapter(create(fallback), fallback);
        }
        var client = networkClients.Create(serverCredential);
        return new OwnedHttpStorageAdapter(create(client), client);
    }

    private static Uri ParseServer(string value, int defaultPort)
    {
        var uri = value.Contains("://", StringComparison.Ordinal)
            ? new Uri(value)
            : new Uri($"tcp://{value}");
        return uri.IsDefaultPort ? new UriBuilder(uri) { Port = defaultPort }.Uri : uri;
    }

    private static Uri RequireUri(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        ? uri
        : throw new ArgumentException("端点必须是完整 URL。", nameof(endpoint));

    private static string CombineLocal(string endpoint, string rootPath) =>
        string.IsNullOrWhiteSpace(rootPath) ? endpoint : Path.Combine(endpoint, rootPath);

    private static string RequireSecret(string secret) => !string.IsNullOrWhiteSpace(secret)
        ? secret
        : throw new InvalidOperationException("该连接缺少安全凭据，请重新保存凭据。");
}
