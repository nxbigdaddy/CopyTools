using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed record PluginDiagnostic(string Path, string Message);

public sealed record PluginCatalog(
    IReadOnlyList<LoadedPlugin> Plugins,
    IReadOnlyDictionary<string, IPluginStorageAdapterFactory> StorageFactories,
    IReadOnlyDictionary<string, IFileVerificationProvider> VerificationProviders,
    IReadOnlyList<PluginDiagnostic> Diagnostics)
{
    public static PluginCatalog Load(string directory)
    {
        var loader = new PluginLoader();
        var plugins = new List<LoadedPlugin>();
        var diagnostics = new List<PluginDiagnostic>();
        if (Directory.Exists(directory))
        {
            foreach (var manifest in Directory.EnumerateFiles(directory, "plugin.json", SearchOption.AllDirectories))
            {
                try { plugins.AddRange(loader.LoadManifest(manifest)); }
                catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
                { diagnostics.Add(new PluginDiagnostic(manifest, Sanitize(exception.Message))); }
            }
        }

        var storage = new Dictionary<string, IPluginStorageAdapterFactory>(StringComparer.OrdinalIgnoreCase);
        var verifiers = new Dictionary<string, IFileVerificationProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            foreach (var factory in plugin.Instance.CreateStorageAdapterFactories())
            {
                ValidateId(factory.Id);
                if (!storage.TryAdd(factory.Id, factory))
                    diagnostics.Add(new PluginDiagnostic(plugin.Directory, $"存储适配器 ID 重复：{factory.Id}。"));
            }
            foreach (var verifier in plugin.Instance.CreateVerificationProviders())
            {
                ValidateId(verifier.Id);
                if (!verifiers.TryAdd(verifier.Id, verifier))
                    diagnostics.Add(new PluginDiagnostic(plugin.Directory, $"校验器 ID 重复：{verifier.Id}。"));
            }
        }
        return new PluginCatalog(plugins, storage, verifiers, diagnostics);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new InvalidDataException("插件扩展 ID 格式无效。");
    }

    private static string Sanitize(string message) => message.Replace('\r', ' ').Replace('\n', ' ').Trim() is { Length: > 500 } text
        ? text[..500]
        : message.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
