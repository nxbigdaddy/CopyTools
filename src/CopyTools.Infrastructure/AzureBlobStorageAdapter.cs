using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

/// <summary>Azure Blob Storage 适配器，使用共享密钥（SharedKey）授权。</summary>
public sealed class AzureBlobStorageAdapter : IStorageAdapter
{
    private readonly Uri _base;
    private readonly string _accountName;
    private readonly byte[] _accountKey;
    private readonly string _container;
    private readonly string _rootPath;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public AzureBlobStorageAdapter(
        string endpoint,
        string accountName,
        string accountKey,
        string container,
        string rootPath = "",
        HttpClient? client = null)
    {
        _base = new Uri(endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/");
        _accountName = accountName;
        _accountKey = Convert.FromBase64String(accountKey);
        _container = container;
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

    public string DisplayName => $"Azure Blob：{_container}";

    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var prefix = StoragePath.Combine(_rootPath, relativeDirectory);
        if (prefix.Length > 0)
        {
            prefix += "/";
        }

        var query = new List<string> { "restype=container", "comp=list" };
        if (prefix.Length > 0)
        {
            query.Add("prefix=" + Uri.EscapeDataString(prefix));
        }

        if (!recursive)
        {
            query.Add("delimiter=" + Uri.EscapeDataString("/"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Resolve(string.Empty, query));
        using var response = await SendAsync(request, string.Empty, query, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var items = new List<StorageItem>();
        foreach (var blob in document.Descendants("Blob"))
        {
            var name = blob.Element("Name")?.Value ?? string.Empty;
            var isDirectory = name.EndsWith("/", StringComparison.Ordinal);
            _ = long.TryParse(blob.Element("Properties")?.Element("Content-Length")?.Value, out var length);
            _ = DateTimeOffset.TryParse(
                blob.Element("Properties")?.Element("Last-Modified")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var modified);
            items.Add(new StorageItem(
                StripRoot(name).TrimEnd('/'),
                isDirectory,
                isDirectory ? 0 : length,
                modified == default ? null : modified.UtcDateTime,
                null));
        }

        return items;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Resolve(Key(relativePath), null));
        var response = await SendAsync(request, Key(relativePath), null, cancellationToken);
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
        using var request = new HttpRequestMessage(HttpMethod.Put, Resolve(Key(relativePath), null));
        request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        request.Content = new ProgressStreamContent(content, length, progress);
        using var response = await SendAsync(request, Key(relativePath), null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Resolve(Key(relativePath), null));
        using var response = await SendAsync(request, Key(relativePath), null, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Resolve(Key(targetRelativePath), null));
        request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        request.Headers.TryAddWithoutValidation(
            "x-ms-copy-source",
            Uri.EscapeDataString($"/{_accountName}/{_container}/{Key(sourceRelativePath)}"));
        using var copyResponse = await SendAsync(request, Key(targetRelativePath), null, cancellationToken);
        copyResponse.EnsureSuccessStatusCode();
        await DeleteAsync(sourceRelativePath, false, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private string Key(string relativePath) => StoragePath.Combine(_rootPath, relativePath);

    private Uri Resolve(string blobKey, IReadOnlyList<string>? query)
    {
        var escaped = string.Join('/', blobKey.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var path = escaped.Length == 0 ? _container : $"{_container}/{escaped}";
        var uri = new Uri(_base, path);
        return query is { Count: > 0 }
            ? new UriBuilder(uri) { Query = string.Join("&", query) }.Uri
            : uri;
    }

    private string StripRoot(string name) =>
        name.StartsWith(_rootPath, StringComparison.Ordinal)
            ? name[_rootPath.Length..].TrimStart('/')
            : name.TrimStart('/');

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string blobKey,
        IReadOnlyList<string>? query,
        CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("x-ms-date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("x-ms-version", "2021-06-08");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "SharedKey",
            $"{_accountName}:{ComputeSignature(request, blobKey, query)}");
        return await _client.SendAsync(request, cancellationToken);
    }

    private string ComputeSignature(HttpRequestMessage request, string blobKey, IReadOnlyList<string>? query)
    {
        var stringToSign = BuildStringToSign(request, blobKey, query);
        using var hmac = new HMACSHA256(_accountKey);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }

    private string BuildStringToSign(
        HttpRequestMessage request,
        string blobKey,
        IReadOnlyList<string>? query)
    {
        var contentEncoding = Header(request, "Content-Encoding");
        var contentLanguage = Header(request, "Content-Language");
        var contentLength = request.Content?.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var contentMd5 = Header(request, "Content-MD5");
        var contentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
        var date = string.Empty;
        var ifModifiedSince = Header(request, "If-Modified-Since");
        var ifMatch = Header(request, "If-Match");
        var ifNoneMatch = Header(request, "If-None-Match");
        var ifUnmodifiedSince = Header(request, "If-Unmodified-Since");
        var range = Header(request, "Range");

        var canonicalHeaders = new StringBuilder();
        foreach (var entry in request.Headers
                     .Where(header => header.Key.StartsWith("x-ms-", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(header => header.Key.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var value = string.Join(",", entry.Value).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            canonicalHeaders.Append(entry.Key.ToLowerInvariant()).Append(':').Append(value).Append('\n');
        }

        return $"{request.Method.Method}\n{contentEncoding}\n{contentLanguage}\n{contentLength}\n{contentMd5}\n"
            + $"{contentType}\n{date}\n{ifModifiedSince}\n{ifMatch}\n{ifNoneMatch}\n{ifUnmodifiedSince}\n{range}\n"
            + canonicalHeaders
            + BuildCanonicalResource(blobKey, query);
    }

    private string BuildCanonicalResource(string blobKey, IReadOnlyList<string>? query)
    {
        var path = $"/{_accountName}/{_container}";
        if (blobKey.Length > 0)
        {
            path += "/" + blobKey;
        }

        var builder = new StringBuilder(path);
        if (query is { Count: > 0 })
        {
            var parameters = query
                .Select(item => item.Split('=', 2))
                .Select(parts => (Name: parts[0].ToLowerInvariant(), Value: parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty))
                .GroupBy(part => part.Name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}:{string.Join(",", group.Select(part => part.Value).OrderBy(value => value, StringComparer.Ordinal))}");
            builder.Append('\n').Append(string.Join("\n", parameters));
        }

        return builder.ToString();
    }

    private static string Header(HttpRequestMessage request, string name)
    {
        if (request.Headers.TryGetValues(name, out var values))
        {
            return string.Join(",", values);
        }

        if (request.Content?.Headers.TryGetValues(name, out var contentValues) == true)
        {
            return string.Join(",", contentValues);
        }

        return string.Empty;
    }
}
