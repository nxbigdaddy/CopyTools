using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class BaiduNetdiskStorageAdapter : IStorageAdapter
{
    private const int UploadBlockSize = 4 * 1024 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _accessToken;
    private readonly string _rootPath;
    private readonly Uri _fileEndpoint;
    private readonly Uri _uploadEndpoint;

    public BaiduNetdiskStorageAdapter(
        string accessToken,
        string rootPath = "",
        HttpClient? client = null,
        Uri? fileEndpoint = null,
        Uri? uploadEndpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        _accessToken = accessToken;
        _rootPath = StoragePath.NormalizeRelative(rootPath);
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
        _fileEndpoint = fileEndpoint ?? new Uri("https://pan.baidu.com/rest/2.0/xpan/file");
        _uploadEndpoint = uploadEndpoint ?? new Uri("https://d.pcs.baidu.com/rest/2.0/pcs/superfile2");
    }

    public string DisplayName => "百度网盘";

    public StorageCapabilities Capabilities => StorageCapabilities.All;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var output = new List<StorageItem>();
        await ListDirectoryAsync(ToAbsolutePath(relativeDirectory), recursive, output, cancellationToken);
        return output;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var entry = await FindEntryAsync(ToAbsolutePath(relativePath), cancellationToken)
            ?? throw new FileNotFoundException("百度网盘文件不存在。", relativePath);
        if (entry.IsDirectory)
        {
            throw new InvalidOperationException("目录不能作为文件流读取。");
        }

        using var response = await _client.GetAsync(
            BuildFileUri("filemetas", new Dictionary<string, string>
            {
                ["fsids"] = JsonSerializer.Serialize(new[] { entry.Id }),
                ["dlink"] = "1",
            }), cancellationToken);
        var payload = await ReadSuccessfulJsonAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var list = document.RootElement.GetProperty("list");
        if (list.GetArrayLength() == 0 || !list[0].TryGetProperty("dlink", out var dlink))
        {
            throw new InvalidDataException("百度网盘未返回下载地址。");
        }

        var downloadUri = AddQuery(new Uri(dlink.GetString()!), "access_token", _accessToken);
        var downloadResponse = await _client.GetAsync(
            downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        downloadResponse.EnsureSuccessStatusCode();
        return new HttpResponseStream(
            await downloadResponse.Content.ReadAsStreamAsync(cancellationToken), downloadResponse);
    }

    public async Task WriteAsync(
        string relativePath,
        Stream content,
        long? length = null,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var absolutePath = ToAbsolutePath(relativePath);
        await using var source = await SeekableUploadSource.CreateAsync(content, length, cancellationToken);
        var hashes = await ComputeBlockHashesAsync(source.Stream, source.StartPosition, source.Length, cancellationToken);
        source.Stream.Position = source.StartPosition;

        var precreate = await PostFormAsync(
            BuildFileUri("precreate"),
            new Dictionary<string, string>
            {
                ["path"] = absolutePath,
                ["size"] = source.Length.ToString(CultureInfo.InvariantCulture),
                ["isdir"] = "0",
                ["autoinit"] = "1",
                ["block_list"] = JsonSerializer.Serialize(hashes),
                ["rtype"] = overwrite ? "2" : "0",
            }, cancellationToken);
        using var precreateDocument = JsonDocument.Parse(precreate);
        var root = precreateDocument.RootElement;
        if (root.TryGetProperty("return_type", out var returnType) && returnType.GetInt32() == 2)
        {
            progress?.Report(source.Length);
            return;
        }

        var uploadId = root.TryGetProperty("uploadid", out var uploadIdElement)
            ? uploadIdElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            throw new InvalidDataException("百度网盘预创建响应缺少 uploadid。");
        }

        var requiredParts = ReadRequiredParts(root, hashes.Count);
        var buffer = new byte[UploadBlockSize];
        foreach (var partIndex in requiredParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = (long)partIndex * UploadBlockSize;
            var count = (int)Math.Min(UploadBlockSize, source.Length - offset);
            source.Stream.Position = source.StartPosition + offset;
            await ReadExactlyAsync(source.Stream, buffer.AsMemory(0, count), cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                BuildUploadUri(absolutePath, uploadId, partIndex))
            {
                Content = new ByteArrayContent(buffer, 0, count),
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            using var response = await _client.SendAsync(request, cancellationToken);
            await ReadSuccessfulJsonAsync(response, cancellationToken);
            progress?.Report(count);
        }

        await PostFormAsync(
            BuildFileUri("create"),
            new Dictionary<string, string>
            {
                ["path"] = absolutePath,
                ["size"] = source.Length.ToString(CultureInfo.InvariantCulture),
                ["isdir"] = "0",
                ["uploadid"] = uploadId,
                ["block_list"] = JsonSerializer.Serialize(hashes),
                ["rtype"] = overwrite ? "2" : "0",
            }, cancellationToken);
    }

    public async Task DeleteAsync(
        string relativePath,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        await FileManagerAsync("delete", new[]
        {
            new Dictionary<string, string> { ["path"] = ToAbsolutePath(relativePath) },
        }, cancellationToken);
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        await PostFormAsync(
            BuildFileUri("create"),
            new Dictionary<string, string>
            {
                ["path"] = ToAbsolutePath(relativePath),
                ["size"] = "0",
                ["isdir"] = "1",
                ["block_list"] = "[]",
                ["rtype"] = "0",
            }, cancellationToken);
    }

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var target = ToAbsolutePath(targetRelativePath);
        var slash = target.LastIndexOf('/');
        var destination = slash <= 0 ? "/" : target[..slash];
        var name = target[(slash + 1)..];
        await FileManagerAsync("move", new[]
        {
            new Dictionary<string, string>
            {
                ["path"] = ToAbsolutePath(sourceRelativePath),
                ["dest"] = destination,
                ["newname"] = name,
                ["ondup"] = overwrite ? "overwrite" : "fail",
            },
        }, cancellationToken);
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
        string absoluteDirectory,
        bool recursive,
        ICollection<StorageItem> output,
        CancellationToken cancellationToken)
    {
        var start = 0;
        const int pageSize = 1000;
        while (true)
        {
            using var response = await _client.GetAsync(
                BuildFileUri("list", new Dictionary<string, string>
                {
                    ["dir"] = absoluteDirectory,
                    ["start"] = start.ToString(CultureInfo.InvariantCulture),
                    ["limit"] = pageSize.ToString(CultureInfo.InvariantCulture),
                    ["order"] = "name",
                    ["desc"] = "0",
                }), cancellationToken);
            var payload = await ReadSuccessfulJsonAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var list = document.RootElement.GetProperty("list");
            foreach (var value in list.EnumerateArray())
            {
                var entry = ParseEntry(value);
                output.Add(new StorageItem(
                    RemoveRoot(entry.Path),
                    entry.IsDirectory,
                    entry.IsDirectory ? 0 : entry.Length,
                    entry.ModifiedUtc,
                    entry.ETag));
                if (recursive && entry.IsDirectory)
                {
                    await ListDirectoryAsync(entry.Path, true, output, cancellationToken);
                }
            }

            if (list.GetArrayLength() < pageSize)
            {
                break;
            }

            start += pageSize;
        }
    }

    private async Task<BaiduEntry?> FindEntryAsync(string absolutePath, CancellationToken cancellationToken)
    {
        var slash = absolutePath.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : absolutePath[..slash];
        var name = absolutePath[(slash + 1)..];
        using var response = await _client.GetAsync(
            BuildFileUri("list", new Dictionary<string, string>
            {
                ["dir"] = parent,
                ["start"] = "0",
                ["limit"] = "1000",
            }), cancellationToken);
        var payload = await ReadSuccessfulJsonAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(payload);
        foreach (var value in document.RootElement.GetProperty("list").EnumerateArray())
        {
            var entry = ParseEntry(value);
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    private async Task FileManagerAsync(
        string operation,
        IReadOnlyCollection<Dictionary<string, string>> fileList,
        CancellationToken cancellationToken)
    {
        await PostFormAsync(
            BuildFileUri("filemanager", new Dictionary<string, string> { ["opera"] = operation }),
            new Dictionary<string, string>
            {
                ["async"] = "0",
                ["filelist"] = JsonSerializer.Serialize(fileList),
            }, cancellationToken);
    }

    private async Task<string> PostFormAsync(
        Uri uri,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        using var response = await _client.SendAsync(request, cancellationToken);
        return await ReadSuccessfulJsonAsync(response, cancellationToken);
    }

    private Uri BuildFileUri(string method, IReadOnlyDictionary<string, string>? values = null)
    {
        var query = new Dictionary<string, string>
        {
            ["method"] = method,
            ["access_token"] = _accessToken,
        };
        if (values is not null)
        {
            foreach (var value in values)
            {
                query[value.Key] = value.Value;
            }
        }

        return BuildUri(_fileEndpoint, query);
    }

    private Uri BuildUploadUri(string path, string uploadId, int partIndex) =>
        BuildUri(_uploadEndpoint, new Dictionary<string, string>
        {
            ["method"] = "upload",
            ["type"] = "tmpfile",
            ["path"] = path,
            ["uploadid"] = uploadId,
            ["partseq"] = partIndex.ToString(CultureInfo.InvariantCulture),
            ["access_token"] = _accessToken,
        });

    private string ToAbsolutePath(string relativePath)
    {
        var combined = StoragePath.Combine(_rootPath, relativePath);
        return string.IsNullOrEmpty(combined) ? "/" : $"/{combined}";
    }

    private string RemoveRoot(string absolutePath)
    {
        var normalized = absolutePath.Trim('/');
        if (string.IsNullOrEmpty(_rootPath))
        {
            return normalized;
        }

        return normalized.StartsWith($"{_rootPath}/", StringComparison.Ordinal)
            ? normalized[(_rootPath.Length + 1)..]
            : normalized;
    }

    private static async Task<List<string>> ComputeBlockHashesAsync(
        Stream stream,
        long startPosition,
        long length,
        CancellationToken cancellationToken)
    {
        var hashes = new List<string>();
        var buffer = new byte[UploadBlockSize];
        stream.Position = startPosition;
        var remaining = length;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            await ReadExactlyAsync(stream, buffer.AsMemory(0, count), cancellationToken);
            hashes.Add(Convert.ToHexString(MD5.HashData(buffer.AsSpan(0, count))).ToLowerInvariant());
            remaining -= count;
        }

        return hashes;
    }

    private static IReadOnlyList<int> ReadRequiredParts(JsonElement root, int partCount)
    {
        if (!root.TryGetProperty("block_list", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return Enumerable.Range(0, partCount).ToArray();
        }

        var parts = new List<int>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
            {
                parts.Add(number);
            }
            else if (item.ValueKind == JsonValueKind.String
                && int.TryParse(item.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                parts.Add(number);
            }
        }

        return parts.Count == 0 ? Enumerable.Range(0, partCount).ToArray() : parts;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var readTotal = 0;
        while (readTotal < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[readTotal..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("百度网盘上传输入流提前结束。");
            }

            readTotal += read;
        }
    }

    private static BaiduEntry ParseEntry(JsonElement value)
    {
        var timestamp = value.TryGetProperty("server_mtime", out var mtime) && mtime.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : (DateTime?)null;
        return new BaiduEntry(
            value.GetProperty("fs_id").GetInt64(),
            value.GetProperty("server_filename").GetString() ?? string.Empty,
            value.GetProperty("path").GetString() ?? string.Empty,
            value.TryGetProperty("isdir", out var isDirectory) && isDirectory.GetInt32() == 1,
            value.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
            timestamp,
            value.TryGetProperty("md5", out var md5) ? md5.GetString() : null);
    }

    private static async Task<string> ReadSuccessfulJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("errno", out var error)
            && error.ValueKind == JsonValueKind.Number
            && error.GetInt32() != 0)
        {
            var message = document.RootElement.TryGetProperty("errmsg", out var detail)
                ? detail.GetString()
                : null;
            throw new InvalidOperationException($"百度网盘接口错误 {error.GetInt32()}：{message ?? "未提供详情"}");
        }

        return payload;
    }

    private static Uri AddQuery(Uri uri, string key, string value)
    {
        var builder = new UriBuilder(uri);
        var prefix = string.IsNullOrEmpty(builder.Query) ? string.Empty : $"{builder.Query.TrimStart('?')}&";
        builder.Query = $"{prefix}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
        return builder.Uri;
    }

    private static Uri BuildUri(Uri endpoint, IReadOnlyDictionary<string, string> values)
    {
        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join("&", values.Select(value =>
                $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}")),
        };
        return builder.Uri;
    }

    private sealed record BaiduEntry(
        long Id,
        string Name,
        string Path,
        bool IsDirectory,
        long Length,
        DateTime? ModifiedUtc,
        string? ETag);

    private sealed class SeekableUploadSource : IAsyncDisposable
    {
        private readonly string? _temporaryPath;

        private SeekableUploadSource(Stream stream, long startPosition, long length, string? temporaryPath)
        {
            Stream = stream;
            StartPosition = startPosition;
            Length = length;
            _temporaryPath = temporaryPath;
        }

        public Stream Stream { get; }
        public long StartPosition { get; }
        public long Length { get; }

        public static async Task<SeekableUploadSource> CreateAsync(
            Stream input,
            long? suppliedLength,
            CancellationToken cancellationToken)
        {
            if (input.CanSeek)
            {
                var available = input.Length - input.Position;
                var actualLength = suppliedLength ?? available;
                if (actualLength < 0 || actualLength > available)
                {
                    throw new ArgumentOutOfRangeException(nameof(suppliedLength), "上传长度超出输入流可用范围。");
                }

                return new SeekableUploadSource(input, input.Position, actualLength, null);
            }

            var path = Path.Combine(Path.GetTempPath(), $"copytools-baidu-{Guid.NewGuid():N}.upload");
            var temporary = new FileStream(
                path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                await input.CopyToAsync(temporary, 1024 * 1024, cancellationToken);
                if (suppliedLength is not null && suppliedLength != temporary.Length)
                {
                    throw new InvalidDataException("上传长度与输入流实际长度不一致。");
                }

                temporary.Position = 0;
                return new SeekableUploadSource(temporary, 0, temporary.Length, path);
            }
            catch
            {
                await temporary.DisposeAsync();
                File.Delete(path);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_temporaryPath is null)
            {
                return;
            }

            await Stream.DisposeAsync();
            File.Delete(_temporaryPath);
        }
    }
}
