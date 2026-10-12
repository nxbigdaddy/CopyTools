using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class S3StorageAdapter : IStorageAdapter
{
    private const int PartSize = 16 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> NoQuery = new Dictionary<string, string>();
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string? _sessionToken;
    private readonly string _region;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly Uri _endpoint;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public S3StorageAdapter(
        string endpoint,
        string region,
        string bucket,
        string accessKey,
        string secretKey,
        string prefix = "",
        string? sessionToken = null,
        HttpClient? client = null)
    {
        _endpoint = new Uri(endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/");
        _region = region;
        _bucket = bucket;
        _accessKey = accessKey;
        _secretKey = secretKey;
        _prefix = StoragePath.NormalizeRelative(prefix);
        _sessionToken = sessionToken;
        _client = client ?? new HttpClient();
        _ownsClient = client is null;
    }

    public string DisplayName => $"S3：{_bucket}@{_endpoint.Host}";
    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories | StorageCapabilities.Resume;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var prefix = Key(relativeDirectory);
        if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith("/", StringComparison.Ordinal)) prefix += "/";
        var items = new List<StorageItem>();
        string? continuation = null;
        do
        {
            var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["list-type"] = "2",
                ["prefix"] = prefix,
            };
            if (!recursive) query["delimiter"] = "/";
            if (!string.IsNullOrEmpty(continuation)) query["continuation-token"] = continuation;
            using var response = await SendAsync(HttpMethod.Get, "", query, EmptyHash, null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            XNamespace ns = xml.Root?.Name.Namespace ?? XNamespace.None;
            foreach (var commonPrefix in xml.Descendants(ns + "CommonPrefixes"))
            {
                var value = commonPrefix.Element(ns + "Prefix")?.Value;
                if (value is not null) items.Add(new StorageItem(RemovePrefix(value).TrimEnd('/'), true, 0, null));
            }

            foreach (var content in xml.Descendants(ns + "Contents"))
            {
                var key = content.Element(ns + "Key")?.Value ?? string.Empty;
                if (key == prefix) continue;
                _ = long.TryParse(content.Element(ns + "Size")?.Value, out var size);
                _ = DateTime.TryParse(content.Element(ns + "LastModified")?.Value, out var modified);
                items.Add(new StorageItem(
                    RemovePrefix(key).TrimEnd('/'), key.EndsWith("/", StringComparison.Ordinal), size,
                    modified == default ? null : modified.ToUniversalTime(),
                    content.Element(ns + "ETag")?.Value));
            }

            continuation = xml.Descendants(ns + "IsTruncated").FirstOrDefault()?.Value == "true"
                ? xml.Descendants(ns + "NextContinuationToken").FirstOrDefault()?.Value : null;
        }
        while (!string.IsNullOrEmpty(continuation));
        return items;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, Key(relativePath), NoQuery, EmptyHash, null, cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
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
        if (length is null) throw new ArgumentException("S3 上传必须提供长度。", nameof(length));
        if (length <= PartSize)
        {
            var bytes = await ReadExactAsync(content, (int)length.Value, cancellationToken);
            var hash = HashHex(bytes);
            using var response = await SendAsync(HttpMethod.Put, Key(relativePath), NoQuery, hash,
                new ByteArrayContent(bytes), cancellationToken);
            response.EnsureSuccessStatusCode();
            progress?.Report(bytes.Length);
            return;
        }

        var key = Key(relativePath);
        using var create = await SendAsync(HttpMethod.Post, key, new SortedDictionary<string, string> { ["uploads"] = string.Empty },
            EmptyHash, new ByteArrayContent([]), cancellationToken);
        create.EnsureSuccessStatusCode();
        var createXml = XDocument.Parse(await create.Content.ReadAsStringAsync(cancellationToken));
        var uploadId = createXml.Descendants().First(element => element.Name.LocalName == "UploadId").Value;
        var parts = new List<(int Number, string ETag)>();
        try
        {
            var partNumber = 1;
            long offset = 0;
            while (offset < length)
            {
                var count = (int)Math.Min(PartSize, length.Value - offset);
                var bytes = await ReadExactAsync(content, count, cancellationToken);
                var query = new SortedDictionary<string, string>
                {
                    ["partNumber"] = partNumber.ToString(CultureInfo.InvariantCulture),
                    ["uploadId"] = uploadId,
                };
                using var part = await SendAsync(HttpMethod.Put, key, query, HashHex(bytes),
                    new ByteArrayContent(bytes), cancellationToken);
                part.EnsureSuccessStatusCode();
                parts.Add((partNumber, part.Headers.ETag?.Tag ?? throw new InvalidDataException("S3 分片响应缺少 ETag。")));
                offset += count;
                partNumber++;
                progress?.Report(count);
            }

            var completeXml = new XElement("CompleteMultipartUpload",
                parts.Select(part => new XElement("Part",
                    new XElement("PartNumber", part.Number), new XElement("ETag", part.ETag))));
            var completeBytes = Encoding.UTF8.GetBytes(completeXml.ToString(SaveOptions.DisableFormatting));
            using var complete = await SendAsync(HttpMethod.Post, key,
                new SortedDictionary<string, string> { ["uploadId"] = uploadId },
                HashHex(completeBytes), new ByteArrayContent(completeBytes), cancellationToken);
            complete.EnsureSuccessStatusCode();
        }
        catch
        {
            using var abort = await SendAsync(HttpMethod.Delete, key,
                new SortedDictionary<string, string> { ["uploadId"] = uploadId },
                EmptyHash, null, CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, Key(relativePath), NoQuery, EmptyHash, null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var key = Key(relativePath).TrimEnd('/') + "/";
        using var response = await SendAsync(HttpMethod.Put, key, NoQuery, EmptyHash, new ByteArrayContent([]), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, BuildUri(Key(targetRelativePath), NoQuery));
        request.Headers.TryAddWithoutValidation("x-amz-copy-source", $"/{_bucket}/{EscapePath(Key(sourceRelativePath))}");
        Sign(request, EmptyHash, DateTime.UtcNow);
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await DeleteAsync(sourceRelativePath, cancellationToken: cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient) _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string key,
        IReadOnlyDictionary<string, string> query,
        string payloadHash,
        HttpContent? content,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var request = new HttpRequestMessage(method, BuildUri(key, query)) { Content = content };
        Sign(request, payloadHash, DateTime.UtcNow);
        return await _client.SendAsync(request, completion, cancellationToken);
    }

    private void Sign(HttpRequestMessage request, string payloadHash, DateTime utcNow)
    {
        var timestamp = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        request.Headers.Host = request.RequestUri!.Authority;
        request.Headers.TryAddWithoutValidation("x-amz-date", timestamp);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (!string.IsNullOrEmpty(_sessionToken)) request.Headers.TryAddWithoutValidation("x-amz-security-token", _sessionToken);

        var headers = request.Headers
            .Where(header => header.Key.Equals("host", StringComparison.OrdinalIgnoreCase)
                || header.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(',', header.Value).Trim(), StringComparer.Ordinal);
        var signedHeaders = string.Join(';', headers.Keys.OrderBy(key => key, StringComparer.Ordinal));
        var canonicalHeaders = string.Concat(headers.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}:{item.Value}\n"));
        var canonicalRequest = $"{request.Method.Method}\n{request.RequestUri.AbsolutePath}\n"
            + $"{request.RequestUri.Query.TrimStart('?')}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{date}/{_region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{HashHex(Encoding.UTF8.GetBytes(canonicalRequest))}";
        var dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + _secretKey), date);
        var regionKey = Hmac(dateKey, _region);
        var serviceKey = Hmac(regionKey, "s3");
        var signingKey = Hmac(serviceKey, "aws4_request");
        var signature = Convert.ToHexString(Hmac(signingKey, stringToSign)).ToLowerInvariant();
        request.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private Uri BuildUri(string key, IReadOnlyDictionary<string, string> query)
    {
        var path = $"{Uri.EscapeDataString(_bucket)}/{EscapePath(key)}".TrimEnd('/');
        var queryString = string.Join('&', query.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        return new Uri(_endpoint, path + (queryString.Length == 0 ? string.Empty : "?" + queryString));
    }

    private string Key(string relativePath) => StoragePath.Combine(_prefix, relativePath);
    private string RemovePrefix(string key) => string.IsNullOrEmpty(_prefix) ? key
        : key.StartsWith($"{_prefix}/", StringComparison.Ordinal) ? key[(_prefix.Length + 1)..] : key;
    private static string EscapePath(string path) => string.Join('/', StoragePath.NormalizeRelative(path)
        .Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var bytes = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("S3 上传输入流提前结束。");
            offset += read;
        }

        return bytes;
    }
    private static string HashHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static readonly string EmptyHash = HashHex([]);
}
