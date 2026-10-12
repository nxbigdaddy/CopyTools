using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class WebDavStorageAdapter : IStorageAdapter
{
    private static readonly XNamespace Dav = "DAV:";
    private readonly Uri _root;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public WebDavStorageAdapter(Uri root, HttpClient? client = null, NetworkCredential? credential = null)
    {
        _root = root.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? root : new Uri(root.AbsoluteUri + "/");
        if (client is null)
        {
            var handler = new HttpClientHandler { PreAuthenticate = true };
            if (credential is not null)
            {
                handler.Credentials = credential;
            }

            _client = new HttpClient(handler);
            _ownsClient = true;
        }
        else
        {
            _client = client;
        }
    }

    public string DisplayName => $"WebDAV：{_root.Host}";

    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), Resolve(relativeDirectory));
        request.Headers.TryAddWithoutValidation("Depth", recursive ? "infinity" : "1");
        request.Content = new StringContent(
            "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop>"
            + "<d:resourcetype/><d:getcontentlength/><d:getlastmodified/><d:getetag/>"
            + "</d:prop></d:propfind>");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var items = new List<StorageItem>();
        var requestedPath = StoragePath.NormalizeRelative(relativeDirectory);
        foreach (var element in document.Descendants(Dav + "response"))
        {
            var href = element.Element(Dav + "href")?.Value;
            if (href is null)
            {
                continue;
            }

            var relative = RelativeFromHref(href);
            if (string.Equals(relative.TrimEnd('/'), requestedPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var properties = element.Descendants(Dav + "prop").FirstOrDefault();
            var directory = properties?.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null;
            _ = long.TryParse(properties?.Element(Dav + "getcontentlength")?.Value, out var length);
            _ = DateTime.TryParse(properties?.Element(Dav + "getlastmodified")?.Value, out var modified);
            items.Add(new StorageItem(
                relative.TrimEnd('/'), directory, directory ? 0 : length,
                modified == default ? null : modified.ToUniversalTime(),
                properties?.Element(Dav + "getetag")?.Value));
        }

        return items;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync(Resolve(relativePath), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
        using var request = new HttpRequestMessage(HttpMethod.Put, Resolve(relativePath));
        if (!overwrite)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }

        request.Content = new ProgressStreamContent(content, length, progress);
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        using var response = await _client.DeleteAsync(Resolve(relativePath), cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), Resolve(relativePath));
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode is not HttpStatusCode.MethodNotAllowed and not HttpStatusCode.Conflict)
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
        using var request = new HttpRequestMessage(new HttpMethod("MOVE"), Resolve(sourceRelativePath));
        request.Headers.TryAddWithoutValidation("Destination", Resolve(targetRelativePath).AbsoluteUri);
        request.Headers.TryAddWithoutValidation("Overwrite", overwrite ? "T" : "F");
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

    private Uri Resolve(string relativePath) => new(_root, EscapePath(StoragePath.NormalizeRelative(relativePath)));

    private string RelativeFromHref(string href)
    {
        var absolute = new Uri(_root, href);
        var rootPath = Uri.UnescapeDataString(_root.AbsolutePath).TrimEnd('/');
        var path = Uri.UnescapeDataString(absolute.AbsolutePath);
        return path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
            ? path[rootPath.Length..].TrimStart('/')
            : path.TrimStart('/');
    }

    private static string EscapePath(string path) => string.Join('/',
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}
