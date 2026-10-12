using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class PluginLoader
{
    public const int ApiMajorVersion = 1;

    public IReadOnlyList<LoadedPlugin> Load(string pluginsDirectory)
    {
        var root = Path.GetFullPath(pluginsDirectory);
        if (!Directory.Exists(root)) return [];
        var loaded = new List<LoadedPlugin>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
        {
            var pluginDirectory = Path.GetDirectoryName(manifestPath)!;
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"插件清单无效：{manifestPath}");
            if (!manifest.Enabled) continue;
            if (manifest.ApiMajorVersion != ApiMajorVersion)
                throw new InvalidDataException($"插件 {manifest.Id} 的 API 主版本不兼容。");
            ValidateIdentifier(manifest.Id);
            var assemblyPath = Path.GetFullPath(Path.Combine(pluginDirectory, manifest.Assembly));
            if (!assemblyPath.StartsWith(Path.TrimEndingDirectorySeparator(pluginDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"插件程序集越出插件目录：{manifest.Id}。");
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("插件程序集不存在。", assemblyPath);
            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadMainAssembly(assemblyPath);
            var type = assembly.GetType(manifest.Type, throwOnError: true, ignoreCase: false)!;
            if (Activator.CreateInstance(type) is not ICopyToolsPlugin plugin)
                throw new InvalidDataException($"插件类型未实现 ICopyToolsPlugin：{manifest.Type}。");
            if (plugin.ApiVersion.Major != ApiMajorVersion || !string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal))
                throw new InvalidDataException($"插件 {manifest.Id} 的运行时身份或 API 版本不匹配。");
            loaded.Add(new LoadedPlugin(manifest, plugin, pluginDirectory));
        }

        return loaded;
    }

    public IReadOnlyList<LoadedPlugin> LoadManifest(string manifestPath)
    {
        var fullManifest = Path.GetFullPath(manifestPath);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(fullManifest),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is { Enabled: false }) return [];
        return LoadCore([fullManifest]);
    }

    private IReadOnlyList<LoadedPlugin> LoadCore(IEnumerable<string> manifestPaths)
    {
        var loaded = new List<LoadedPlugin>();
        foreach (var manifestPath in manifestPaths)
        {
            loaded.Add(LoadOne(manifestPath));
        }
        return loaded;
    }

    private LoadedPlugin LoadOne(string manifestPath)
    {
        var pluginDirectory = Path.GetDirectoryName(manifestPath)!;
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"插件清单无效：{manifestPath}");
        if (!manifest.Enabled) throw new InvalidDataException($"插件 {manifest.Id} 已停用。");
        if (manifest.ApiMajorVersion != ApiMajorVersion)
            throw new InvalidDataException($"插件 {manifest.Id} 的 API 主版本不兼容。");
        ValidateIdentifier(manifest.Id);
        var assemblyPath = Path.GetFullPath(Path.Combine(pluginDirectory, manifest.Assembly));
        if (!assemblyPath.StartsWith(Path.TrimEndingDirectorySeparator(pluginDirectory) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"插件程序集越出插件目录：{manifest.Id}。");
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("插件程序集不存在。", assemblyPath);
        var context = new PluginLoadContext(assemblyPath);
        var assembly = context.LoadMainAssembly(assemblyPath);
        var type = assembly.GetType(manifest.Type, throwOnError: true, ignoreCase: false)!;
        if (Activator.CreateInstance(type) is not ICopyToolsPlugin plugin)
            throw new InvalidDataException($"插件类型未实现 ICopyToolsPlugin：{manifest.Type}。");
        if (plugin.ApiVersion.Major != ApiMajorVersion || !string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"插件 {manifest.Id} 的运行时身份或 API 版本不匹配。");
        return new LoadedPlugin(manifest, plugin, pluginDirectory);
    }

    private static void ValidateIdentifier(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100
            || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new InvalidDataException("插件 ID 只能包含 ASCII 字母、数字、点、短横线和下划线。");
    }

    private sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);
        public Assembly LoadMainAssembly(string path) => LoadManagedAssembly(path);
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "CopyTools.Engine" or "CopyTools.Domain") return null;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadManagedAssembly(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        private Assembly LoadManagedAssembly(string path)
        {
            using var assembly = new MemoryStream(File.ReadAllBytes(path), writable: false);
            var pdbPath = Path.ChangeExtension(path, ".pdb");
            if (File.Exists(pdbPath))
            {
                using var symbols = new MemoryStream(File.ReadAllBytes(pdbPath), writable: false);
                return LoadFromStream(assembly, symbols);
            }
            return LoadFromStream(assembly);
        }
    }
}
