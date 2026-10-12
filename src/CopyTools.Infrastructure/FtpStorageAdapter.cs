using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class FtpStorageAdapter : IStorageAdapter
{
    private readonly string _host;
    private readonly int _port;
    private readonly NetworkCredential _credential;
    private readonly bool _useTls;
    private readonly string _root;

    public FtpStorageAdapter(
        string host,
        int port,
        NetworkCredential credential,
        bool useTls = true,
        string rootDirectory = "")
    {
        _host = host;
        _port = port;
        _credential = credential;
        _useTls = useTls;
        _root = StoragePath.NormalizeRelative(rootDirectory);
    }

    public string DisplayName => $"FTP{(_useTls ? "S" : string.Empty)}：{_host}:{_port}";

    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories | StorageCapabilities.Resume;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        await using var session = await FtpSession.ConnectAsync(
            _host, _port, _credential, _useTls, cancellationToken);
        var start = CombineRoot(relativeDirectory);
        var result = new List<StorageItem>();
        await ListDirectoryAsync(session, start, recursive, result, cancellationToken);
        return result;
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"copytools-ftp-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var session = await FtpSession.ConnectAsync(
                _host, _port, _credential, _useTls, cancellationToken);
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await session.DownloadAsync(CombineRoot(relativePath), output, cancellationToken);
            }

            return new DeleteOnDisposeFileStream(temporary);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    public async Task WriteAsync(
        string relativePath,
        Stream content,
        long? length = null,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = await FtpSession.ConnectAsync(
            _host, _port, _credential, _useTls, cancellationToken);
        var path = CombineRoot(relativePath);
        var offset = 0L;
        if (content.CanSeek)
        {
            offset = await session.GetSizeAsync(path, cancellationToken);
            if (!overwrite && offset > 0) throw new IOException($"FTP 目标已存在：{relativePath}。");
            if (offset > content.Length) offset = 0;
            if (offset == content.Length)
            {
                progress?.Report(offset);
                return;
            }
            content.Position = offset;
            if (offset > 0) progress?.Report(offset);
        }
        await session.UploadAsync(path, content, progress, cancellationToken, offset);
    }

    public async Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        await using var session = await FtpSession.ConnectAsync(
            _host, _port, _credential, _useTls, cancellationToken);
        var path = CombineRoot(relativePath);
        if (recursive)
        {
            var items = new List<StorageItem>();
            await ListDirectoryAsync(session, path, true, items, cancellationToken);
            foreach (var file in items.Where(item => !item.IsDirectory).OrderByDescending(item => item.RelativePath.Length))
            {
                await session.CommandAsync($"DELE {CombineRoot(file.RelativePath)}", cancellationToken, 250);
            }

            foreach (var directory in items.Where(item => item.IsDirectory).OrderByDescending(item => item.RelativePath.Length))
            {
                await session.CommandAsync($"RMD {CombineRoot(directory.RelativePath)}", cancellationToken, 250);
            }
        }

        var response = await session.CommandAsync($"DELE {path}", cancellationToken, 250, 550);
        if (response.Code == 550)
        {
            await session.CommandAsync($"RMD {path}", cancellationToken, 250);
        }
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        await using var session = await FtpSession.ConnectAsync(
            _host, _port, _credential, _useTls, cancellationToken);
        var current = string.Empty;
        foreach (var segment in CombineRoot(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = StoragePath.Combine(current, segment);
            await session.CommandAsync($"MKD /{current}", cancellationToken, 257, 550);
        }
    }

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        await using var session = await FtpSession.ConnectAsync(
            _host, _port, _credential, _useTls, cancellationToken);
        await session.CommandAsync($"RNFR {CombineRoot(sourceRelativePath)}", cancellationToken, 350);
        await session.CommandAsync($"RNTO {CombineRoot(targetRelativePath)}", cancellationToken, 250);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task ListDirectoryAsync(
        FtpSession session,
        string directory,
        bool recursive,
        ICollection<StorageItem> output,
        CancellationToken cancellationToken)
    {
        var entries = await session.ListAsync(directory, cancellationToken);
        foreach (var entry in entries)
        {
            var full = StoragePath.Combine(directory, entry.Name);
            var relative = RemoveRoot(full);
            output.Add(new StorageItem(relative, entry.IsDirectory, entry.Length, entry.ModifiedUtc));
            if (recursive && entry.IsDirectory && entry.Name is not "." and not "..")
            {
                await ListDirectoryAsync(session, full, true, output, cancellationToken);
            }
        }
    }

    private string CombineRoot(string relativePath) => "/" + StoragePath.Combine(_root, relativePath);

    private string RemoveRoot(string fullPath)
    {
        var normalized = StoragePath.NormalizeRelative(fullPath);
        return string.IsNullOrEmpty(_root)
            ? normalized
            : normalized.StartsWith($"{_root}/", StringComparison.OrdinalIgnoreCase)
                ? normalized[(_root.Length + 1)..]
                : normalized;
    }

    private sealed class FtpSession : IAsyncDisposable
    {
        private readonly TcpClient _controlClient;
        private Stream _controlStream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly bool _useTls;
        private readonly string _host;

        private FtpSession(TcpClient client, Stream stream, string host, bool useTls)
        {
            _controlClient = client;
            _controlStream = stream;
            _host = host;
            _useTls = useTls;
            _reader = CreateReader(stream);
            _writer = CreateWriter(stream);
        }

        public static async Task<FtpSession> ConnectAsync(
            string host,
            int port,
            NetworkCredential credential,
            bool useTls,
            CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            var session = new FtpSession(client, client.GetStream(), host, useTls);
            await session.ReadResponseAsync(cancellationToken, 220);
            if (useTls)
            {
                await session.CommandAsync("AUTH TLS", cancellationToken, 234);
                var ssl = new SslStream(session._controlStream, false);
                await ssl.AuthenticateAsClientAsync(host);
                session.ReplaceStream(ssl);
            }

            var user = await session.CommandAsync($"USER {credential.UserName}", cancellationToken, 230, 331);
            if (user.Code == 331)
            {
                await session.CommandAsync($"PASS {credential.Password}", cancellationToken, 230);
            }

            await session.CommandAsync("TYPE I", cancellationToken, 200);
            if (useTls)
            {
                await session.CommandAsync("PBSZ 0", cancellationToken, 200);
                await session.CommandAsync("PROT P", cancellationToken, 200);
            }

            return session;
        }

        public async Task<IReadOnlyList<FtpListEntry>> ListAsync(string directory, CancellationToken cancellationToken)
        {
            var data = await OpenPassiveDataAsync(cancellationToken);
            await CommandWithoutResponseAsync($"MLSD {directory}", cancellationToken);
            var opening = await ReadResponseAsync(cancellationToken, 125, 150);
            await using var dataStream = await ProtectDataAsync(data.GetStream());
            using var reader = new StreamReader(dataStream, Encoding.UTF8);
            var entries = new List<FtpListEntry>();
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var separator = line.IndexOf(' ');
                if (separator < 0)
                {
                    continue;
                }

                var facts = line[..separator].Split(';', StringSplitOptions.RemoveEmptyEntries);
                var name = line[(separator + 1)..].Trim();
                var map = facts.Select(fact => fact.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
                var isDirectory = map.TryGetValue("type", out var type) && type.StartsWith("dir", StringComparison.OrdinalIgnoreCase);
                var length = map.TryGetValue("size", out var sizeText) && long.TryParse(sizeText, out var parsedLength)
                    ? parsedLength
                    : 0;
                DateTime? modified = null;
                if (map.TryGetValue("modify", out var modify)
                    && DateTime.TryParseExact(modify, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    modified = parsed;
                }

                entries.Add(new FtpListEntry(name, isDirectory, length, modified));
            }

            data.Dispose();
            await ReadResponseAsync(cancellationToken, 226, 250);
            return entries;
        }

        public async Task DownloadAsync(string path, Stream target, CancellationToken cancellationToken)
        {
            var data = await OpenPassiveDataAsync(cancellationToken);
            await CommandWithoutResponseAsync($"RETR {path}", cancellationToken);
            await ReadResponseAsync(cancellationToken, 125, 150);
            await using (var dataStream = await ProtectDataAsync(data.GetStream()))
            {
                await dataStream.CopyToAsync(target, cancellationToken);
            }

            data.Dispose();
            await ReadResponseAsync(cancellationToken, 226, 250);
        }

        public async Task UploadAsync(
            string path,
            Stream source,
            IProgress<long>? progress,
            CancellationToken cancellationToken,
            long resumeOffset = 0)
        {
            var data = await OpenPassiveDataAsync(cancellationToken);
            if (resumeOffset > 0) await CommandAsync($"REST {resumeOffset}", cancellationToken, 350);
            await CommandWithoutResponseAsync($"STOR {path}", cancellationToken);
            await ReadResponseAsync(cancellationToken, 125, 150);
            await using (var dataStream = await ProtectDataAsync(data.GetStream()))
            {
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await dataStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(read);
                }

                await dataStream.FlushAsync(cancellationToken);
            }

            data.Dispose();
            await ReadResponseAsync(cancellationToken, 226, 250);
        }

        public async Task<long> GetSizeAsync(string path, CancellationToken cancellationToken)
        {
            var response = await CommandAsync($"SIZE {path}", cancellationToken, 213, 450, 500, 502, 550);
            if (response.Code != 213) return 0;
            var line = response.Message.Split('\n')[^1];
            return line.Length > 4 && long.TryParse(line[4..].Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var length) ? length : 0;
        }

        public async Task<FtpResponse> CommandAsync(
            string command,
            CancellationToken cancellationToken,
            params int[] acceptedCodes)
        {
            await CommandWithoutResponseAsync(command, cancellationToken);
            return await ReadResponseAsync(cancellationToken, acceptedCodes);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await CommandAsync("QUIT", CancellationToken.None, 221);
            }
            catch (IOException)
            {
            }

            await _controlStream.DisposeAsync();
            _controlClient.Dispose();
        }

        private async Task<TcpClient> OpenPassiveDataAsync(CancellationToken cancellationToken)
        {
            var response = await CommandAsync("EPSV", cancellationToken, 229);
            var start = response.Message.LastIndexOf('(');
            var end = response.Message.LastIndexOf(')');
            if (start < 0 || end <= start)
            {
                throw new IOException("FTP EPSV 响应格式无效。");
            }

            var fields = response.Message[(start + 1)..end].Split('|');
            if (fields.Length < 4 || !int.TryParse(fields[^2], out var port))
            {
                throw new IOException("FTP EPSV 端口无效。");
            }

            var data = new TcpClient();
            await data.ConnectAsync(_host, port, cancellationToken);
            return data;
        }

        private async Task<Stream> ProtectDataAsync(Stream stream)
        {
            if (!_useTls)
            {
                return stream;
            }

            var ssl = new SslStream(stream, false);
            await ssl.AuthenticateAsClientAsync(_host);
            return ssl;
        }

        private async Task CommandWithoutResponseAsync(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _writer.WriteLineAsync(command);
            await _writer.FlushAsync(cancellationToken);
        }

        private async Task<FtpResponse> ReadResponseAsync(
            CancellationToken cancellationToken,
            params int[] acceptedCodes)
        {
            var line = await _reader.ReadLineAsync(cancellationToken)
                ?? throw new IOException("FTP 控制连接已关闭。");
            if (line.Length < 3 || !int.TryParse(line[..3], out var code))
            {
                throw new IOException($"FTP 响应无效：{line}");
            }

            var lines = new List<string> { line };
            if (line.Length > 3 && line[3] == '-')
            {
                var terminator = $"{code} ";
                while (true)
                {
                    var next = await _reader.ReadLineAsync(cancellationToken)
                        ?? throw new IOException("FTP 多行响应未完成。");
                    lines.Add(next);
                    if (next.StartsWith(terminator, StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }

            var response = new FtpResponse(code, string.Join("\n", lines));
            if (!acceptedCodes.Contains(code))
            {
                throw new IOException($"FTP 命令失败（{code}）：{response.Message}");
            }

            return response;
        }

        private void ReplaceStream(Stream stream)
        {
            _controlStream = stream;
            _reader = CreateReader(stream);
            _writer = CreateWriter(stream);
        }

        private static StreamReader CreateReader(Stream stream) => new(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        private static StreamWriter CreateWriter(Stream stream) => new(stream, Encoding.ASCII, 4096, leaveOpen: true)
        {
            NewLine = "\r\n",
        };
    }

    private sealed record FtpResponse(int Code, string Message);
    private sealed record FtpListEntry(string Name, bool IsDirectory, long Length, DateTime? ModifiedUtc);

    private sealed class DeleteOnDisposeFileStream(string path) : FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
}
