using CopyTools.Domain;

namespace CopyTools.Engine;

public interface IStorageAdapter : IAsyncDisposable
{
    string DisplayName { get; }

    StorageCapabilities Capabilities { get; }

    Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    Task WriteAsync(
        string relativePath,
        Stream content,
        long? length = null,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default);

    Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default);
}
