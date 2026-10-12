using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed partial class NetworkShareStorageAdapter : IStorageAdapter
{
    private const int ResourceTypeDisk = 1;
    private const int ErrorSessionCredentialConflict = 1219;
    private readonly string _shareRoot;
    private readonly LocalStorageAdapter _inner;
    private readonly bool _connectedByThisInstance;

    public NetworkShareStorageAdapter(string shareRoot, NetworkCredential? credential = null)
    {
        _shareRoot = Path.GetFullPath(shareRoot);
        if (!_shareRoot.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("网络共享路径必须是 UNC 路径。", nameof(shareRoot));
        }

        if (credential is not null)
        {
            var resource = new NetworkResource { Type = ResourceTypeDisk, RemoteName = _shareRoot };
            var result = WNetAddConnection2(ref resource, credential.Password, credential.UserName, 0);
            if (result == 0)
            {
                _connectedByThisInstance = true;
            }
            else if (result != ErrorSessionCredentialConflict)
            {
                throw new Win32Exception(result, "连接 Windows 网络共享失败。");
            }
        }

        _inner = new LocalStorageAdapter(_shareRoot);
    }

    public string DisplayName => $"Windows 网络共享：{_shareRoot}";
    public StorageCapabilities Capabilities => _inner.Capabilities;
    public Task<IReadOnlyList<StorageItem>> ListAsync(string relativeDirectory = "", bool recursive = true, CancellationToken cancellationToken = default) =>
        _inner.ListAsync(relativeDirectory, recursive, cancellationToken);
    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _inner.OpenReadAsync(relativePath, cancellationToken);
    public Task WriteAsync(string relativePath, Stream content, long? length = null, bool overwrite = true, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(relativePath, content, length, overwrite, progress, cancellationToken);
    public Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(relativePath, recursive, cancellationToken);
    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _inner.CreateDirectoryAsync(relativePath, cancellationToken);
    public Task MoveAsync(string sourceRelativePath, string targetRelativePath, bool overwrite = false, CancellationToken cancellationToken = default) =>
        _inner.MoveAsync(sourceRelativePath, targetRelativePath, overwrite, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        if (_connectedByThisInstance)
        {
            _ = WNetCancelConnection2(_shareRoot, 0, false);
        }
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NetworkResource networkResource,
        string? password,
        string? userName,
        int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetworkResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
