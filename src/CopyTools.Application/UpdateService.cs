using System.Security.Cryptography;
using System.Text.Json;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class UpdateService(Func<HttpClient>? httpClientFactory = null)
{
    public async Task<UpdateCheckResult> CheckAsync(
        string manifestLocation,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        var (manifest, baseLocation) = await ReadManifestAsync(manifestLocation, cancellationToken);
        if (manifest.FormatVersion != 1) throw new InvalidDataException($"不支持的更新清单版本：{manifest.FormatVersion}。");
        if (!Version.TryParse(manifest.Version, out var latest)) throw new InvalidDataException("更新清单版本号无效。");
        ValidateHash(manifest.Sha256);
        if (manifest.Size <= 0) throw new InvalidDataException("更新包大小无效。");
        var resolvedUrl = ResolveLocation(baseLocation, manifest.DownloadUrl);
        manifest = manifest with { DownloadUrl = resolvedUrl };
        return new UpdateCheckResult(currentVersion, latest, latest > currentVersion, manifest);
    }

    public async Task<string> DownloadAsync(
        UpdateManifest manifest,
        string targetPath,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateHash(manifest.Sha256);
        var fullPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + $".{Guid.NewGuid():N}.partial";
        try
        {
            await using var source = await OpenReadAsync(manifest.DownloadUrl, cancellationToken);
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long copied = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    copied += read;
                    progress?.Report(new CopyProgress(0, 1, copied, manifest.Size, Path.GetFileName(fullPath), "正在下载更新", copied, manifest.Size));
                }
                await target.FlushAsync(cancellationToken);
                if (copied != manifest.Size) throw new InvalidDataException($"更新包大小不匹配：期望 {manifest.Size}，实际 {copied}。");
                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(manifest.Sha256)))
                    throw new InvalidDataException("更新包 SHA-256 校验失败，文件可能损坏或被篡改。");
            }
            File.Move(temporary, fullPath, true);
            return fullPath;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<(UpdateManifest Manifest, string BaseLocation)> ReadManifestAsync(
        string location, CancellationToken cancellationToken)
    {
        await using var stream = await OpenReadAsync(location, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidDataException("更新清单为空或格式无效。");
        return (manifest, location);
    }

    private async Task<Stream> OpenReadAsync(string location, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var client = httpClientFactory?.Invoke() ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            try
            {
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                return new OwnedResponseStream(await response.Content.ReadAsStreamAsync(cancellationToken), response, client,
                    disposeClient: httpClientFactory is null);
            }
            catch { if (httpClientFactory is null) client.Dispose(); throw; }
        }
        var path = uri?.IsFile == true ? uri.LocalPath : Path.GetFullPath(location);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static string ResolveLocation(string manifestLocation, string downloadLocation)
    {
        if (Uri.TryCreate(downloadLocation, UriKind.Absolute, out var absolute)) return absolute.IsFile ? absolute.LocalPath : absolute.ToString();
        if (Uri.TryCreate(manifestLocation, UriKind.Absolute, out var manifestUri) && manifestUri.Scheme is "http" or "https")
            return new Uri(manifestUri, downloadLocation).ToString();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestLocation))!, downloadLocation));
    }

    private static void ValidateHash(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("更新清单 SHA-256 无效。");
    }

    private sealed class OwnedResponseStream(Stream inner, IDisposable response, IDisposable client, bool disposeClient) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); if (disposeClient) client.Dispose(); } base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); response.Dispose(); if (disposeClient) client.Dispose(); GC.SuppressFinalize(this); }
    }
}
