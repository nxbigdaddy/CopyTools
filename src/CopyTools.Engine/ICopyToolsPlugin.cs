using CopyTools.Domain;

namespace CopyTools.Engine;

public interface ICopyToolsPlugin
{
    string Id { get; }
    string Name { get; }
    Version ApiVersion { get; }
    IReadOnlyCollection<IPluginStorageAdapterFactory> CreateStorageAdapterFactories();
    IReadOnlyCollection<IFileVerificationProvider> CreateVerificationProviders();
}

public interface IPluginStorageAdapterFactory
{
    string Id { get; }
    string DisplayName { get; }
    IStorageAdapter Create(StorageConnectionProfile profile, string userName, string secret);
}

public interface IFileVerificationProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task<bool> VerifyAsync(Stream source, Stream target, CancellationToken cancellationToken = default);
}

public sealed record PluginManifest(
    string Id,
    string Name,
    string Assembly,
    string Type,
    int ApiMajorVersion = 1,
    bool Enabled = true);

public sealed record LoadedPlugin(
    PluginManifest Manifest,
    ICopyToolsPlugin Instance,
    string Directory);
