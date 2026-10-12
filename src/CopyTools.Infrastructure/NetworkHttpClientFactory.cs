using System.Net;
using System.Runtime.Versioning;
using CopyTools.Domain;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class NetworkHttpClientFactory(
    ApplicationSettings settings,
    WindowsCredentialStore credentialStore)
{
    private ApplicationSettings _settings = settings.Normalize();
    private readonly object _sync = new();

    public void Update(ApplicationSettings settings)
    {
        lock (_sync) _settings = settings.Normalize();
    }

    public HttpClient Create(NetworkCredential? serverCredential = null)
    {
        ApplicationSettings settings;
        lock (_sync) settings = _settings;
        var handler = new HttpClientHandler
        {
            PreAuthenticate = serverCredential is not null,
            Credentials = serverCredential,
            UseProxy = settings.UseSystemProxy || !string.IsNullOrWhiteSpace(settings.ProxyUrl),
        };
        if (!string.IsNullOrWhiteSpace(settings.ProxyUrl))
        {
            if (!Uri.TryCreate(settings.ProxyUrl, UriKind.Absolute, out var proxyUri)
                || proxyUri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("代理地址必须是完整的 http:// 或 https:// URL。");
            var proxy = new WebProxy(proxyUri);
            var credential = credentialStore.Read(settings.ProxyCredentialTarget);
            if (credential is { Secret.Length: > 0 })
                proxy.Credentials = new NetworkCredential(
                    string.IsNullOrWhiteSpace(settings.ProxyUserName) ? credential.Value.UserName : settings.ProxyUserName,
                    credential.Value.Secret);
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
    }
}
