using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class GoogleDriveStorageAdapter : IStorageAdapter
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const int ChunkSize = 8 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _apiRoot;
    private readonly Uri _uploadRoot;
    private readonly string _rootFolderId;

    public GoogleDriveStorageAdapter(
        string accessToken,
        string rootFolderId = "root",
        HttpClient? client = null,
        Uri? apiRoot = null,
        Uri? uploadRoot = null)
    {
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
        _apiRoot = apiRoot ?? new Uri("https://www.googleapis.com/drive/v3/");
        _uploadRoot = uploadRoot ?? new Uri("https://www.googleapis.com/upload/drive/v3/");
        _rootFolderId = rootFolderId;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public string DisplayName => "Google Drive";
    public StorageCapabilities Capabilities => StorageCapabilities.All;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var folderId = await ResolveFolderIdAsync(relativeDirectory, create: false, cancellationToken);
        var output = new List<StorageItem>();
        await ListFolderAsync(folderId, StoragePath.NormalizeRelative(relativeDirectory), recursive, output, cancellationToken);
        return output;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var item = await ResolveItemAsync(relativePath, cancellationToken)
            ?? throw new FileNotFoundException("Google Drive 文件不存在。", relativePath);
        var response = await _client.GetAsync(
            new Uri(_apiRoot, $"files/{Uri.EscapeDataString(item.Id)}?alt=media"),
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
        if (length is null)
        {
            throw new ArgumentException("Google Drive 可恢复上传必须提供长度。", nameof(length));
        }

        var normalized = StoragePath.NormalizeRelative(relativePath);
        var parentPath = StoragePath.NormalizeRelative(Path.GetDirectoryName(normalized) ?? string.Empty);
        var parentId = await ResolveFolderIdAsync(parentPath, create: true, cancellationToken);
        var existing = await FindChildAsync(parentId, Path.GetFileName(normalized), cancellationToken);
        if (existing is not null && !overwrite)
        {
            throw new IOException("Google Drive 目标文件已存在。");
        }

        var sessionMethod = existing is null ? HttpMethod.Post : new HttpMethod("PATCH");
        var endpoint = existing is null
            ? new Uri(_uploadRoot, "files?uploadType=resumable")
            : new Uri(_uploadRoot, $"files/{Uri.EscapeDataString(existing.Id)}?uploadType=resumable");
        object metadata = existing is null
            ? new { name = Path.GetFileName(normalized), parents = new[] { parentId } }
            : new { name = Path.GetFileName(normalized), parents = Array.Empty<string>() };
        using var request = new HttpRequestMessage(sessionMethod, endpoint)
        {
            Content = JsonContent.Create(metadata),
        };
        request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "application/octet-stream");
        request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var uploadUri = response.Headers.Location
            ?? throw new InvalidDataException("Google Drive 上传响应缺少 Location。");
        await UploadChunksAsync(uploadUri, content, length.Value, progress, cancellationToken);
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var item = await ResolveItemAsync(relativePath, cancellationToken);
        if (item is null)
        {
            return;
        }

        using var response = await _client.DeleteAsync(
            new Uri(_apiRoot, $"files/{Uri.EscapeDataString(item.Id)}"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _ = await ResolveFolderIdAsync(relativePath, create: true, cancellationToken);

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var item = await ResolveItemAsync(sourceRelativePath, cancellationToken)
            ?? throw new FileNotFoundException("Google Drive 源对象不存在。", sourceRelativePath);
        var target = StoragePath.NormalizeRelative(targetRelativePath);
        var parentId = await ResolveFolderIdAsync(
            StoragePath.NormalizeRelative(Path.GetDirectoryName(target) ?? string.Empty), true, cancellationToken);
        var endpoint = new Uri(_apiRoot,
            $"files/{Uri.EscapeDataString(item.Id)}?addParents={Uri.EscapeDataString(parentId)}"
            + $"&removeParents={Uri.EscapeDataString(item.ParentId ?? string.Empty)}");
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), endpoint)
        {
            Content = JsonContent.Create(new { name = Path.GetFileName(target) }),
        };
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient) _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ListFolderAsync(
        string folderId,
        string relativePath,
        bool recursive,
        ICollection<StorageItem> output,
        CancellationToken cancellationToken)
    {
        string? pageToken = null;
        do
        {
            var query = $"'{EscapeQuery(folderId)}' in parents and trashed=false";
            var endpoint = $"files?q={Uri.EscapeDataString(query)}&pageSize=1000"
                + "&fields=nextPageToken,files(id,name,mimeType,size,modifiedTime,md5Checksum,parents)";
            if (!string.IsNullOrEmpty(pageToken)) endpoint += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            using var response = await _client.GetAsync(new Uri(_apiRoot, endpoint), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach (var element in document.RootElement.GetProperty("files").EnumerateArray())
            {
                var item = ParseItem(element);
                var path = StoragePath.Combine(relativePath, item.Name);
                output.Add(new StorageItem(path, item.IsDirectory, item.Length, item.ModifiedUtc, item.Hash));
                if (recursive && item.IsDirectory)
                {
                    await ListFolderAsync(item.Id, path, true, output, cancellationToken);
                }
            }

            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

    private async Task<string> ResolveFolderIdAsync(string relativePath, bool create, CancellationToken cancellationToken)
    {
        var current = _rootFolderId;
        foreach (var segment in StoragePath.NormalizeRelative(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var child = await FindChildAsync(current, segment, cancellationToken);
            if (child is null)
            {
                if (!create) throw new DirectoryNotFoundException($"Google Drive 目录不存在：{relativePath}");
                using var response = await _client.PostAsJsonAsync(
                    new Uri(_apiRoot, "files"),
                    new { name = segment, mimeType = FolderMimeType, parents = new[] { current } },
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var created = await response.Content.ReadFromJsonAsync<CreatedItem>(cancellationToken: cancellationToken)
                    ?? throw new InvalidDataException("Google Drive 创建目录响应无效。");
                current = created.Id;
            }
            else
            {
                if (!child.IsDirectory) throw new IOException($"Google Drive 路径片段不是目录：{segment}");
                current = child.Id;
            }
        }

        return current;
    }

    private async Task<DriveItem?> ResolveItemAsync(string relativePath, CancellationToken cancellationToken)
    {
        var normalized = StoragePath.NormalizeRelative(relativePath);
        var parent = await ResolveFolderIdAsync(
            StoragePath.NormalizeRelative(Path.GetDirectoryName(normalized) ?? string.Empty), false, cancellationToken);
        return await FindChildAsync(parent, Path.GetFileName(normalized), cancellationToken);
    }

    private async Task<DriveItem?> FindChildAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        var query = $"'{EscapeQuery(parentId)}' in parents and name='{EscapeQuery(name)}' and trashed=false";
        var endpoint = $"files?q={Uri.EscapeDataString(query)}&pageSize=2"
            + "&fields=files(id,name,mimeType,size,modifiedTime,md5Checksum,parents)";
        using var response = await _client.GetAsync(new Uri(_apiRoot, endpoint), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var files = document.RootElement.GetProperty("files");
        return files.GetArrayLength() == 0 ? null : ParseItem(files[0]);
    }

    private async Task UploadChunksAsync(Uri uri, Stream content, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkSize];
        long offset = 0;
        while (offset < length)
        {
            var count = (int)Math.Min(buffer.Length, length - offset);
            var readTotal = 0;
            while (readTotal < count)
            {
                var read = await content.ReadAsync(buffer.AsMemory(readTotal, count - readTotal), cancellationToken);
                if (read == 0) throw new EndOfStreamException("Google Drive 上传输入流提前结束。");
                readTotal += read;
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, uri)
            {
                Content = new ByteArrayContent(buffer, 0, readTotal),
            };
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + readTotal - 1, length);
            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            offset += readTotal;
            progress?.Report(readTotal);
        }
    }

    private static DriveItem ParseItem(JsonElement item)
    {
        var mime = item.GetProperty("mimeType").GetString() ?? string.Empty;
        var parent = item.TryGetProperty("parents", out var parents) && parents.GetArrayLength() > 0
            ? parents[0].GetString() : null;
        return new DriveItem(
            item.GetProperty("id").GetString()!, item.GetProperty("name").GetString()!, mime == FolderMimeType,
            item.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var length) ? length : 0,
            item.TryGetProperty("modifiedTime", out var modified) && modified.TryGetDateTime(out var timestamp)
                ? timestamp.ToUniversalTime() : null,
            item.TryGetProperty("md5Checksum", out var hash) ? hash.GetString() : null,
            parent);
    }

    private static string EscapeQuery(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
    private sealed record DriveItem(string Id, string Name, bool IsDirectory, long Length, DateTime? ModifiedUtc, string? Hash, string? ParentId);
    private sealed record CreatedItem(string Id);
}
