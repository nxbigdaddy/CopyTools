using CopyTools.Infrastructure;
using CopyTools.Engine;

namespace CopyTools.Desktop.ViewModels;

public sealed class PluginViewModel(LoadedPlugin plugin)
{
    public string Name => plugin.Instance.Name;
    public string Id => plugin.Instance.Id;
    public string Version => plugin.Instance.ApiVersion.ToString();
    public int StorageAdapters => plugin.Instance.CreateStorageAdapterFactories().Count;
    public int VerificationProviders => plugin.Instance.CreateVerificationProviders().Count;
    public string Directory => plugin.Directory;
}

public sealed class PluginDiagnosticViewModel(PluginDiagnostic diagnostic)
{
    public string Path => diagnostic.Path;
    public string Message => diagnostic.Message;
}

public sealed record PluginVerificationOption(string? Id, string Name, string Description);
