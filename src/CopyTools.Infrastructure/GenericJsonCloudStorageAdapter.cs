using System.Net;
using System.Text.Json;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

/// <summary>
/// 面向 OAuth 承载令牌 + JSON REST 网盘（阿里云盘、夸克、天翼云盘、123 云盘等）的通用适配器。
/// 具体服务端点与响应字段存在差异，本适配器按通用约定实现协议级调用，实际账号验收需目标环境。
/// </summary>
public sealed class GenericJsonCloudStorageAdapter : IStorageAdapter
{
    private readonly Uri _base;
    private readonly string _bearerToken;
    private readonly string _rootPath;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public GenericJsonCloudStorageAdapter(
        string endpoint,
        string bearerToken,
        string rootPath = "",
        HttpClient? client = null)
    {
        _base = new Uri(endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/");
        _bearerToken = bearerToken;
        _rootPath = StoragePath.NormalizeRelative(rootPath);
        if (client is null)
        {
            _client = new HttpClient();
            _ownsClient = true;
        }
        else
        {
            _client = client;
        }
    }

    public string DisplayName => $"网盘：{_base.Host}";

    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var key = StoragePath.Combine(_rootPath, relativeDirectory);
        using var request = Create(HttpMethod.Get, "list", new Dictionary<string, string>
        {
            ["path"] = key,
            ["recursive"] = recursive ? "true" : "false",
        });
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var items = new List<StorageItem>();
        if (payload.RootElement.TryGetProperty("items", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var name = entry.TryGetProperty("path", out var pathElement) ? pathElement.GetString() ?? string.Empty : string.Empty;
                var isDirectory = entry.TryGetProperty("isDirectory", out var dirElement) && dirElement.GetBoolean();
                var length = entry.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size) ? size : 0;
                DateTime? modified = entry.TryGetProperty("modifiedUtc", out var modifiedElement)
                    && DateTime.TryParse(modifiedElement.GetString(), out var parsed)
                        ? parsed.ToUniversalTime()
                        : null;
                items.Add(new StorageItem(StripRoot(name), isDirectory, isDirectory ? 0 : length, modified, null));
            }
        }

        return items;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Get, "file", new Dictionary<string, string>
        {
            ["path"] = StoragePath.Combine(_rootPath, relativePath),
        });
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return new HttpResponseStream(await response.Content.ReadAsStreamAsync(cancellationToken), response);
    }

    public async Task WriteAsync(
        string relativePath,
        Stream content,
        long? length = null,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Put, "file", new Dictionary<string, string>
        {
            ["path"] = StoragePath.Combine(_rootPath, relativePath),
            ["overwrite"] = overwrite ? "true" : "false",
        });
        request.Content = new ProgressStreamContent(content, length, progress);
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Delete, "file", new Dictionary<string, string>
        {
            ["path"] = StoragePath.Combine(_rootPath, relativePath),
        });
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Post, "mkdir", new Dictionary<string, string>
        {
            ["path"] = StoragePath.Combine(_rootPath, relativePath),
        });
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Post, "move", new Dictionary<string, string>
        {
            ["from"] = StoragePath.Combine(_rootPath, sourceRelativePath),
            ["to"] = StoragePath.Combine(_rootPath, targetRelativePath),
        });
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private HttpRequestMessage Create(
        HttpMethod method,
        string operation,
        IReadOnlyDictionary<string, string> parameters)
    {
        var query = string.Join("&", parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var request = new HttpRequestMessage(method, new Uri(_base, $"{operation}?{query}"));
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_bearerToken}");
        return request;
    }

    private string StripRoot(string name) =>
        name.StartsWith(_rootPath, StringComparison.Ordinal)
            ? name[_rootPath.Length..].TrimStart('/')
            : name.TrimStart('/');
}
