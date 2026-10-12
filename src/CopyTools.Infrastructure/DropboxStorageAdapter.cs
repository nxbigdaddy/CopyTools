using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class DropboxStorageAdapter : IStorageAdapter
{
    private const int ChunkSize = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _apiRoot;
    private readonly Uri _contentRoot;
    private readonly string _root;

    public DropboxStorageAdapter(
        string accessToken,
        string rootPath = "",
        HttpClient? client = null,
        Uri? apiRoot = null,
        Uri? contentRoot = null)
    {
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
        _apiRoot = apiRoot ?? new Uri("https://api.dropboxapi.com/2/");
        _contentRoot = contentRoot ?? new Uri("https://content.dropboxapi.com/2/");
        _root = StoragePath.NormalizeRelative(rootPath);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public string DisplayName => "Dropbox";
    public StorageCapabilities Capabilities => StorageCapabilities.All;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var output = new List<StorageItem>();
        var response = await PostJsonAsync("files/list_folder", new { path = ApiPath(relativeDirectory), recursive }, cancellationToken);
        while (true)
        {
            using var document = JsonDocument.Parse(response);
            foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
            {
                var tag = entry.GetProperty(".tag").GetString();
                var path = entry.GetProperty("path_display").GetString() ?? string.Empty;
                output.Add(new StorageItem(
                    RemoveRoot(path), tag == "folder",
                    tag == "file" && entry.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    entry.TryGetProperty("server_modified", out var modified) && modified.TryGetDateTime(out var timestamp)
                        ? timestamp.ToUniversalTime() : null,
                    entry.TryGetProperty("content_hash", out var hash) ? hash.GetString() : null));
            }

            if (!document.RootElement.GetProperty("has_more").GetBoolean()) break;
            var cursor = document.RootElement.GetProperty("cursor").GetString();
            response = await PostJsonAsync("files/list_folder/continue", new { cursor }, cancellationToken);
        }

        return output;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_contentRoot, "files/download"));
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = ApiPath(relativePath) }));
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
        if (length is null) throw new ArgumentException("Dropbox 上传必须提供长度。", nameof(length));
        if (length <= 150L * 1024 * 1024)
        {
            using var request = ContentRequest("files/upload", new
            {
                path = ApiPath(relativePath), mode = overwrite ? "overwrite" : "add", autorename = !overwrite, mute = true,
            }, new ProgressStreamContent(content, length, progress));
            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }

        var start = await StartUploadSessionAsync(content, length.Value, progress, cancellationToken);
        var sessionId = start.SessionId;
        var offset = start.Offset;
        while (length.Value - offset > ChunkSize)
        {
            var chunk = await ReadChunkAsync(content, ChunkSize, cancellationToken);
            using var request = ContentRequest("files/upload_session/append_v2", new
            {
                cursor = new { session_id = sessionId, offset }, close = false,
            }, new ByteArrayContent(chunk));
            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            offset += chunk.Length;
            progress?.Report(chunk.Length);
        }

        var finalChunk = await ReadChunkAsync(content, (int)(length.Value - offset), cancellationToken);
        using var finish = ContentRequest("files/upload_session/finish", new
        {
            cursor = new { session_id = sessionId, offset },
            commit = new { path = ApiPath(relativePath), mode = overwrite ? "overwrite" : "add", autorename = !overwrite, mute = true },
        }, new ByteArrayContent(finalChunk));
        using var finishResponse = await _client.SendAsync(finish, cancellationToken);
        finishResponse.EnsureSuccessStatusCode();
        progress?.Report(finalChunk.Length);
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        _ = await PostJsonAsync("files/delete_v2", new { path = ApiPath(relativePath) }, cancellationToken);

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _ = await PostJsonAsync("files/create_folder_v2", new { path = ApiPath(relativePath), autorename = false }, cancellationToken);

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default) =>
        _ = await PostJsonAsync("files/move_v2", new
        {
            from_path = ApiPath(sourceRelativePath), to_path = ApiPath(targetRelativePath), autorename = false, allow_ownership_transfer = false,
        }, cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (_ownsClient) _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<(string SessionId, long Offset)> StartUploadSessionAsync(
        Stream content,
        long length,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var chunk = await ReadChunkAsync(content, (int)Math.Min(ChunkSize, length), cancellationToken);
        using var request = ContentRequest("files/upload_session/start", new { close = false }, new ByteArrayContent(chunk));
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<UploadSession>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Dropbox 上传会话响应无效。");
        progress?.Report(chunk.Length);
        return (session.SessionId, chunk.Length);
    }

    private async Task<string> PostJsonAsync(string endpoint, object body, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsJsonAsync(new Uri(_apiRoot, endpoint), body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private HttpRequestMessage ContentRequest(string endpoint, object argument, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_contentRoot, endpoint)) { Content = content };
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(argument));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return request;
    }

    private static async Task<byte[]> ReadChunkAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read == 0) throw new EndOfStreamException("Dropbox 上传输入流提前结束。");
            total += read;
        }

        return buffer;
    }

    private string ApiPath(string relativePath)
    {
        var combined = StoragePath.Combine(_root, relativePath);
        return string.IsNullOrEmpty(combined) ? string.Empty : "/" + combined;
    }

    private string RemoveRoot(string path)
    {
        var normalized = StoragePath.NormalizeRelative(path);
        return string.IsNullOrEmpty(_root) ? normalized
            : normalized.StartsWith($"{_root}/", StringComparison.OrdinalIgnoreCase) ? normalized[(_root.Length + 1)..] : normalized;
    }

    private sealed record UploadSession([property: System.Text.Json.Serialization.JsonPropertyName("session_id")] string SessionId);
}
