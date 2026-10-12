using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class OneDriveStorageAdapter : IStorageAdapter
{
    private const long SimpleUploadLimit = 250L * 1024 * 1024;
    private const int UploadChunkSize = 10 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _graphRoot;
    private readonly string _rootPath;

    public OneDriveStorageAdapter(
        string accessToken,
        string rootPath = "",
        HttpClient? client = null,
        Uri? graphRoot = null)
    {
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
        _graphRoot = graphRoot ?? new Uri("https://graph.microsoft.com/v1.0/");
        _rootPath = StoragePath.NormalizeRelative(rootPath);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public string DisplayName => "OneDrive / Microsoft Graph";

    public StorageCapabilities Capabilities => StorageCapabilities.All;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var output = new List<StorageItem>();
        await ListDirectoryAsync(Combine(relativeDirectory), recursive, output, cancellationToken);
        return output;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync(
            GraphPath(Combine(relativePath), "content"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
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
        var path = Combine(relativePath);
        if (length is not null && length <= SimpleUploadLimit)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, GraphPath(path, "content"))
            {
                Content = new ProgressStreamContent(content, length, progress),
            };
            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }

        if (length is null)
        {
            throw new ArgumentException("OneDrive 分片上传必须提供内容长度。", nameof(length));
        }

        using var sessionRequest = new HttpRequestMessage(HttpMethod.Post, GraphPath(path, "createUploadSession"))
        {
            Content = JsonContent.Create(new
            {
                item = new Dictionary<string, object>
                {
                    ["@microsoft.graph.conflictBehavior"] = overwrite ? "replace" : "fail",
                    ["name"] = Path.GetFileName(path),
                },
            }),
        };
        using var sessionResponse = await _client.SendAsync(sessionRequest, cancellationToken);
        sessionResponse.EnsureSuccessStatusCode();
        var session = await sessionResponse.Content.ReadFromJsonAsync<UploadSession>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("OneDrive 上传会话响应无效。");
        await UploadChunksAsync(new Uri(session.UploadUrl), content, length.Value, progress, cancellationToken);
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        using var response = await _client.DeleteAsync(GraphPath(Combine(relativePath)), cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var combined = Combine(relativePath);
        var parent = StoragePath.NormalizeRelative(Path.GetDirectoryName(combined) ?? string.Empty);
        var name = Path.GetFileName(combined);
        using var response = await _client.PostAsJsonAsync(
            ChildrenUri(parent),
            new
            {
                name,
                folder = new { },
                conflictBehavior = "fail",
            },
            cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Conflict)
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
        var target = Combine(targetRelativePath);
        var parent = StoragePath.NormalizeRelative(Path.GetDirectoryName(target) ?? string.Empty);
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), GraphPath(Combine(sourceRelativePath)))
        {
            Content = JsonContent.Create(new
            {
                name = Path.GetFileName(target),
                parentReference = new { path = $"/drive/root:/{parent}" },
            }),
        };
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

    private async Task ListDirectoryAsync(
        string path,
        bool recursive,
        ICollection<StorageItem> output,
        CancellationToken cancellationToken)
    {
        Uri? next = ChildrenUri(path);
        while (next is not null)
        {
            using var response = await _client.GetAsync(next, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach (var value in document.RootElement.GetProperty("value").EnumerateArray())
            {
                var name = value.GetProperty("name").GetString() ?? string.Empty;
                var itemPath = StoragePath.Combine(path, name);
                var relative = RemoveRoot(itemPath);
                var directory = value.TryGetProperty("folder", out _);
                var length = value.TryGetProperty("size", out var size) ? size.GetInt64() : 0;
                DateTime? modified = value.TryGetProperty("lastModifiedDateTime", out var timestamp)
                    && timestamp.TryGetDateTime(out var parsed) ? parsed.ToUniversalTime() : null;
                output.Add(new StorageItem(
                    relative, directory, directory ? 0 : length, modified,
                    value.TryGetProperty("eTag", out var etag) ? etag.GetString() : null));
                if (recursive && directory)
                {
                    await ListDirectoryAsync(itemPath, true, output, cancellationToken);
                }
            }

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link)
                ? new Uri(link.GetString()!)
                : null;
        }
    }

    private async Task UploadChunksAsync(
        Uri uploadUrl,
        Stream content,
        long length,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[UploadChunkSize];
        long offset = 0;
        while (offset < length)
        {
            var required = (int)Math.Min(buffer.Length, length - offset);
            var readTotal = 0;
            while (readTotal < required)
            {
                var read = await content.ReadAsync(buffer.AsMemory(readTotal, required - readTotal), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("OneDrive 分片上传的输入流提前结束。");
                }

                readTotal += read;
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(buffer, 0, readTotal),
            };
            request.Content.Headers.ContentLength = readTotal;
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + readTotal - 1, length);
            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            offset += readTotal;
            progress?.Report(readTotal);
        }
    }

    private string Combine(string relativePath) => StoragePath.Combine(_rootPath, relativePath);

    private string RemoveRoot(string path) => string.IsNullOrEmpty(_rootPath)
        ? path
        : path.StartsWith($"{_rootPath}/", StringComparison.OrdinalIgnoreCase)
            ? path[(_rootPath.Length + 1)..]
            : path;

    private Uri ChildrenUri(string path) => GraphPath(path, "children");

    private Uri GraphPath(string path, string? suffix = null)
    {
        var escaped = string.Join('/', StoragePath.NormalizeRelative(path).Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var endpoint = string.IsNullOrEmpty(escaped)
            ? "me/drive/root"
            : $"me/drive/root:/{escaped}:";
        if (!string.IsNullOrEmpty(suffix))
        {
            endpoint += $"/{suffix}";
        }

        return new Uri(_graphRoot, endpoint);
    }

    private sealed record UploadSession(string UploadUrl);
}
