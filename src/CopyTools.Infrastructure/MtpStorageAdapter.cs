using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CopyTools.Domain;
using CopyTools.Engine;

namespace CopyTools.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class MtpStorageAdapter : IStorageAdapter
{
    private const int ShellMyComputer = 17;
    private const int FileOperationFlags = 0x4 | 0x10 | 0x400 | 0x800;
    private readonly string _deviceName;
    private readonly string _rootPath;
    private readonly TimeSpan _operationTimeout;

    public MtpStorageAdapter(string deviceName, string rootPath = "", TimeSpan? operationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        _deviceName = deviceName.Trim();
        _rootPath = StoragePath.NormalizeRelative(rootPath);
        _operationTimeout = operationTimeout ?? TimeSpan.FromMinutes(30);
    }

    public string DisplayName => $"MTP：{_deviceName}";
    public StorageCapabilities Capabilities => StorageCapabilities.List | StorageCapabilities.Read
        | StorageCapabilities.Write | StorageCapabilities.Delete | StorageCapabilities.Move
        | StorageCapabilities.Directories;

    public Task<IReadOnlyList<StorageItem>> ListAsync(
        string relativeDirectory = "",
        bool recursive = true,
        CancellationToken cancellationToken = default) =>
        RunStaAsync<IReadOnlyList<StorageItem>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic folder = ResolveFolder(Combine(relativeDirectory));
            var output = new List<StorageItem>();
            ListFolder(folder, StoragePath.NormalizeRelative(relativeDirectory), recursive, output, cancellationToken);
            Release(folder);
            return output;
        }, cancellationToken);

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"copytools-mtp-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var fileName = Path.GetFileName(StoragePath.NormalizeRelative(relativePath));
            var expectedLength = await RunStaAsync(() =>
            {
                dynamic item = ResolveItem(Combine(relativePath));
                var length = GetLength(item);
                dynamic shell = CreateShell();
                dynamic destination = shell.NameSpace(temporaryDirectory)
                    ?? throw new IOException("无法打开 MTP 临时下载目录。");
                destination.CopyHere(item, FileOperationFlags);
                Release(destination);
                Release(shell);
                Release(item);
                return length;
            }, cancellationToken);
            var temporaryPath = Path.Combine(temporaryDirectory, fileName);
            await WaitForLocalFileAsync(temporaryPath, expectedLength, cancellationToken);
            return new DeleteDirectoryOnDisposeFileStream(temporaryPath, temporaryDirectory);
        }
        catch
        {
            TryDeleteDirectory(temporaryDirectory);
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
        var normalized = StoragePath.NormalizeRelative(relativePath);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"copytools-mtp-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, Path.GetFileName(normalized));
        try
        {
            long written = 0;
            await using (var file = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    progress?.Report(read);
                }
                await file.FlushAsync(cancellationToken);
            }
            if (length is not null && length != written) throw new InvalidDataException("MTP 上传长度与声明长度不一致。");

            var parentPath = StoragePath.NormalizeRelative(Path.GetDirectoryName(normalized) ?? string.Empty);
            var name = Path.GetFileName(normalized);
            if (!overwrite && await ExistsAsync(normalized, cancellationToken))
                throw new IOException($"MTP 目标已存在：{normalized}。");
            if (overwrite && await ExistsAsync(normalized, cancellationToken))
                await DeleteAsync(normalized, cancellationToken: cancellationToken);
            await RunStaAsync(() =>
            {
                dynamic folder = ResolveFolder(Combine(parentPath));
                folder.CopyHere(temporaryPath, FileOperationFlags);
                Release(folder);
                return true;
            }, cancellationToken);
            await WaitForRemoteItemAsync(Combine(parentPath), name, written, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    public Task DeleteAsync(string relativePath, bool recursive = false, CancellationToken cancellationToken = default) =>
        RunStaAsync(() =>
        {
            dynamic item = ResolveItem(Combine(relativePath));
            item.InvokeVerb("delete");
            Release(item);
            return true;
        }, cancellationToken);

    public Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        RunStaAsync(() =>
        {
            var normalized = Combine(relativePath);
            var parentPath = StoragePath.NormalizeRelative(Path.GetDirectoryName(normalized) ?? string.Empty);
            var name = Path.GetFileName(normalized);
            dynamic folder = ResolveFolder(parentPath);
            folder.NewFolder(name, FileOperationFlags);
            Release(folder);
            return true;
        }, cancellationToken);

    public async Task MoveAsync(
        string sourceRelativePath,
        string targetRelativePath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var target = Combine(targetRelativePath);
        var targetParent = StoragePath.NormalizeRelative(Path.GetDirectoryName(target) ?? string.Empty);
        var targetName = Path.GetFileName(target);
        if (!overwrite && await ExistsAsync(target, cancellationToken))
            throw new IOException($"MTP 目标已存在：{targetRelativePath}。");
        await RunStaAsync(() =>
        {
            dynamic source = ResolveItem(Combine(sourceRelativePath));
            dynamic destination = ResolveFolder(targetParent);
            destination.MoveHere(source, FileOperationFlags);
            Release(destination);
            Release(source);
            return true;
        }, cancellationToken);
        var sourceName = Path.GetFileName(StoragePath.NormalizeRelative(sourceRelativePath));
        if (!string.Equals(sourceName, targetName, StringComparison.Ordinal))
        {
            await WaitForRemoteItemAsync(targetParent, sourceName, null, cancellationToken);
            await RunStaAsync(() =>
            {
                dynamic moved = ResolveItem(StoragePath.Combine(targetParent, sourceName));
                moved.Name = targetName;
                Release(moved);
                return true;
            }, cancellationToken);
        }
        await WaitForRemoteItemAsync(targetParent, targetName, null, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        try { await RunStaAsync(() => { dynamic item = ResolveItem(path); Release(item); return true; }, cancellationToken); return true; }
        catch (FileNotFoundException) { return false; }
    }

    private async Task WaitForRemoteItemAsync(
        string parentPath,
        string name,
        long? expectedLength,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _operationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await RunStaAsync(() =>
            {
                dynamic folder = ResolveFolder(parentPath);
                dynamic items = folder.Items();
                foreach (dynamic item in items)
                {
                    if (string.Equals((string)item.Name, name, StringComparison.OrdinalIgnoreCase)
                        && (expectedLength is null || GetLength(item) == expectedLength))
                    {
                        Release(item); Release(items); Release(folder); return true;
                    }
                    Release(item);
                }
                Release(items); Release(folder); return false;
            }, cancellationToken);
            if (found) return;
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException($"等待 MTP 操作完成超时：{name}。");
    }

    private async Task WaitForLocalFileAsync(string path, long expectedLength, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _operationTimeout;
        long previousLength = -1;
        var stableCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var length = new FileInfo(path).Length;
                stableCount = length == previousLength ? stableCount + 1 : 0;
                if ((expectedLength >= 0 && length == expectedLength) || (expectedLength < 0 && stableCount >= 3)) return;
                previousLength = length;
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("等待 MTP 下载完成超时。");
    }

    private void ListFolder(
        dynamic folder,
        string relativeDirectory,
        bool recursive,
        ICollection<StorageItem> output,
        CancellationToken cancellationToken)
    {
        dynamic items = folder.Items();
        foreach (dynamic item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = (string)item.Name;
            var relative = StoragePath.Combine(relativeDirectory, name);
            var isDirectory = (bool)item.IsFolder;
            output.Add(new StorageItem(relative, isDirectory, isDirectory ? 0 : GetLength(item), GetModified(item)));
            if (recursive && isDirectory)
            {
                dynamic childFolder = item.GetFolder;
                ListFolder(childFolder, relative, true, output, cancellationToken);
                Release(childFolder);
            }
            Release(item);
        }
        Release(items);
    }

    private dynamic ResolveFolder(string path)
    {
        dynamic item = ResolveDevice();
        dynamic folder = item.GetFolder;
        Release(item);
        foreach (var segment in StoragePath.NormalizeRelative(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            dynamic items = folder.Items();
            dynamic next = items.Item(segment) ?? throw new DirectoryNotFoundException($"MTP 目录不存在：{path}。");
            dynamic nextFolder = next.GetFolder;
            Release(next); Release(items); Release(folder);
            folder = nextFolder;
        }
        return folder;
    }

    private dynamic ResolveItem(string path)
    {
        var normalized = StoragePath.NormalizeRelative(path);
        var parent = StoragePath.NormalizeRelative(Path.GetDirectoryName(normalized) ?? string.Empty);
        var name = Path.GetFileName(normalized);
        dynamic folder = ResolveFolder(parent);
        dynamic items = folder.Items();
        dynamic item = items.Item(name) ?? throw new FileNotFoundException("MTP 项目不存在。", normalized);
        Release(items); Release(folder);
        return item;
    }

    private dynamic ResolveDevice()
    {
        dynamic shell = CreateShell();
        dynamic computer = shell.NameSpace(ShellMyComputer) ?? throw new IOException("无法打开 Windows 此电脑命名空间。");
        dynamic items = computer.Items();
        foreach (dynamic item in items)
        {
            if ((bool)item.IsFolder && string.Equals((string)item.Name, _deviceName, StringComparison.OrdinalIgnoreCase))
            {
                Release(items); Release(computer); Release(shell); return item;
            }
            Release(item);
        }
        Release(items); Release(computer); Release(shell);
        throw new DriveNotFoundException($"未找到 MTP 设备：{_deviceName}。");
    }

    private static dynamic CreateShell()
    {
        var type = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法创建 Windows Shell 对象。");
    }

    private string Combine(string relativePath) => StoragePath.Combine(_rootPath, relativePath);
    private static long GetLength(dynamic item) { try { return Convert.ToInt64(item.Size, System.Globalization.CultureInfo.InvariantCulture); } catch { return -1; } }
    private static DateTime? GetModified(dynamic item) { try { return ((DateTime)item.ModifyDate).ToUniversalTime(); } catch { return null; } }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static Task<T> RunStaAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { cancellationToken.ThrowIfCancellationRequested(); completion.TrySetResult(action()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true, Name = "CopyTools MTP STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class DeleteDirectoryOnDisposeFileStream : FileStream
    {
        private readonly string _directory;

        public DeleteDirectoryOnDisposeFileStream(string path, string directory)
            : base(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan)
        {
            _directory = directory;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) TryDeleteDirectory(_directory);
        }
    }
}
