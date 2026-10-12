using CopyTools.Application;
using CopyTools.Domain;
using CopyTools.Engine;
using System.Runtime.Versioning;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class StorageConnectionService(
    ICopyToolsRepository repository,
    WindowsCredentialStore credentialStore,
    IReadOnlyDictionary<string, IPluginStorageAdapterFactory>? pluginFactories = null,
    NetworkHttpClientFactory? networkClients = null)
{
    private readonly StorageAdapterFactory _factory = new(credentialStore, pluginFactories, networkClients);

    public Task<IReadOnlyList<StorageConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
        repository.GetStorageConnectionsAsync(cancellationToken);

    public async Task SaveAsync(
        StorageConnectionProfile profile,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        Validate(profile);
        if (!string.IsNullOrEmpty(secret))
        {
            credentialStore.Save(profile.CredentialTarget, profile.UserName, secret);
        }

        await repository.SaveStorageConnectionAsync(profile with { UpdatedAtUtc = DateTime.UtcNow }, cancellationToken);
    }

    public async Task DeleteAsync(StorageConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        await repository.DeleteStorageConnectionAsync(profile.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(profile.CredentialTarget))
        {
            credentialStore.Delete(profile.CredentialTarget);
        }
    }

    public async Task<int> TestAsync(StorageConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        await using var adapter = CreateAdapter(profile);
        var items = await adapter.ListAsync(recursive: false, cancellationToken: cancellationToken);
        return items.Count;
    }

    public IStorageAdapter CreateAdapter(StorageConnectionProfile profile)
    {
        Validate(profile);
        return _factory.Create(profile);
    }

    private static void Validate(StorageConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("连接名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.Endpoint))
        {
            throw new ArgumentException("连接端点或本地路径不能为空。");
        }

        if (profile.Kind is StorageConnectionKind.S3 or StorageConnectionKind.AliyunOss or StorageConnectionKind.TencentCos
            && (string.IsNullOrWhiteSpace(profile.Region) || string.IsNullOrWhiteSpace(profile.Bucket)))
        {
            throw new ArgumentException("对象存储必须填写区域和存储桶。");
        }
    }
}
