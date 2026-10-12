using System.Diagnostics;
using System.Text.Json;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

public sealed class SftpStorageAdapter : IStorageAdapter
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _root;
    private readonly string? _identityFile;
    private readonly string _sftpExecutable;
    private readonly string _scpExecutable;

    public SftpStorageAdapter(
        string host,
        int port,
        string user,
        string rootDirectory = "",
        string? identityFile = null,
        string sftpExecutable = "sftp.exe",
        string scpExecutable = "scp.exe")
    {
        _host = host;
        _port = port;
        _user = user;
        _root = "/" + StoragePath.NormalizeRelative(rootDirectory);
        _identityFile = identityFile;
        _sftpExecutable = sftpExecutable;
        _scpExecutable = scpExecutable;
    }

    public string DisplayName => $"SFTP：{_user}@{_host}:{_port}";

    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories;

    public async Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        var output = await RunBatchAsync($"ls -la {QuoteRemote(Resolve(relativeDirectory))}", cancellationToken);
        return ParseListing(output, relativeDirectory);
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"copytools-sftp-{Guid.NewGuid():N}.tmp");
        await RunScpAsync($"{_user}@{_host}:{QuoteRemote(Resolve(relativePath))}", temporary, upload: false, cancellationToken);
        return new DeleteOnDisposeFileStream(temporary);
    }

    public async Task WriteAsync(
        string relativePath,
        Stream content,
        long? length = null,
        bool overwrite = true,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"copytools-sftp-upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[1024 * 1024];
                long written = 0;
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(read);
                    written += read;
                }

                if (length is not null && length != written)
                {
                    throw new IOException("SFTP 上传长度与声明长度不一致。");
                }
            }

            await RunScpAsync(temporary, $"{_user}@{_host}:{QuoteRemote(Resolve(relativePath))}", true, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        RunBatchAsync($"{(recursive ? "rm -r" : "rm")} {QuoteRemote(Resolve(relativePath))}", cancellationToken);

    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        RunBatchAsync($"mkdir {QuoteRemote(Resolve(relativePath))}", cancellationToken);

    public Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default) =>
        RunBatchAsync($"rename {QuoteRemote(Resolve(sourceRelativePath))} {QuoteRemote(Resolve(targetRelativePath))}", cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<string> RunBatchAsync(string command, CancellationToken cancellationToken)
    {
        var batchPath = Path.Combine(Path.GetTempPath(), $"copytools-sftp-{Guid.NewGuid():N}.batch");
        try
        {
            await File.WriteAllTextAsync(batchPath, command + Environment.NewLine + "bye", cancellationToken);
            var start = CreateStartInfo(_sftpExecutable);
            AddCommonArguments(start);
            start.ArgumentList.Add("-b");
            start.ArgumentList.Add(batchPath);
            start.ArgumentList.Add($"{_user}@{_host}");
            return await RunProcessAsync(start, cancellationToken);
        }
        finally
        {
            if (File.Exists(batchPath))
            {
                File.Delete(batchPath);
            }
        }
    }

    private async Task RunScpAsync(string source, string target, bool upload, CancellationToken cancellationToken)
    {
        var start = CreateStartInfo(_scpExecutable);
        AddCommonArguments(start);
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(target);
        _ = await RunProcessAsync(start, cancellationToken);
    }

    private void AddCommonArguments(ProcessStartInfo start)
    {
        start.ArgumentList.Add("-P");
        start.ArgumentList.Add(_port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("BatchMode=yes");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("StrictHostKeyChecking=yes");
        if (!string.IsNullOrWhiteSpace(_identityFile))
        {
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(_identityFile);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable) => new(executable)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
    };

    private static async Task<string> RunProcessAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"无法启动外部传输程序：{start.FileName}。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new IOException($"SFTP 操作失败（退出码 {process.ExitCode}）：{Sanitize(error)}");
        }

        return output;
    }

    private string Resolve(string relativePath) => "/" + StoragePath.Combine(_root, relativePath);

    private static string QuoteRemote(string path) => $"\"{path.Replace("\"", "\\\"")}\"";

    private static string Sanitize(string error) => error.Length <= 1000 ? error : error[..1000];

    private static IReadOnlyList<StorageItem> ParseListing(string output, string relativeDirectory)
    {
        var items = new List<StorageItem>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 10 || line[0] is not ('-' or 'd'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9 || !long.TryParse(parts[4], out var length))
            {
                continue;
            }

            var name = string.Join(' ', parts.Skip(8));
            if (name is "." or "..")
            {
                continue;
            }

            items.Add(new StorageItem(
                StoragePath.Combine(relativeDirectory, name),
                line[0] == 'd',
                line[0] == 'd' ? 0 : length,
                null));
        }

        return items;
    }

    private sealed class DeleteOnDisposeFileStream(string path) : FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
}
