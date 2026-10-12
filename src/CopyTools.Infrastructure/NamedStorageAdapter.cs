using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

internal sealed class NamedStorageAdapter(string displayName, IStorageAdapter inner) : IStorageAdapter
{
    public string DisplayName => displayName;
    public StorageCapabilities Capabilities => inner.Capabilities;
    public Task<IReadOnlyList<StorageItem>> ListAsync(string relativeDirectory = "", bool recursive = true, CancellationToken cancellationToken = default) =>
        inner.ListAsync(relativeDirectory, recursive, cancellationToken);
    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        inner.OpenReadAsync(relativePath, cancellationToken);
    public Task WriteAsync(string relativePath, Stream content, long? length = null, bool overwrite = true, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(relativePath, content, length, overwrite, progress, cancellationToken);
    public Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(relativePath, recursive, cancellationToken);
    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        inner.CreateDirectoryAsync(relativePath, cancellationToken);
    public Task MoveAsync(string sourceRelativePath, string targetRelativePath, bool overwrite = false, CancellationToken cancellationToken = default) =>
        inner.MoveAsync(sourceRelativePath, targetRelativePath, overwrite, cancellationToken);
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
