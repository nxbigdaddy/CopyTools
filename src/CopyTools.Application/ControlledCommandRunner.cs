using System.Diagnostics;
using System.Text;
using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class ControlledCommandRunner
{
    private const int MaximumCapturedCharacters = 64 * 1024;

    public async Task<ControlledCommandResult> RunAsync(
        ControlledCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FileName);
        if (!Path.IsPathFullyQualified(command.FileName))
        {
            throw new ArgumentException("受控命令必须使用可执行文件的绝对路径。", nameof(command));
        }

        var start = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? Path.GetDirectoryName(command.FileName) ?? Environment.CurrentDirectory
                : Path.GetFullPath(command.WorkingDirectory),
        };
        foreach (var argument in command.EffectiveArguments)
        {
            start.ArgumentList.Add(argument);
        }

        var started = DateTime.UtcNow;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动受控外部命令。");
        using var timeout = new CancellationTokenSource(command.EffectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, linked.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, linked.Token);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = await AwaitOutputAsync(stdoutTask);
        var error = await AwaitOutputAsync(stderrTask);
        var exitCode = process.HasExited ? process.ExitCode : (int?)null;
        return new ControlledCommandResult(
            !timedOut && exitCode is { } code && command.EffectiveSuccessExitCodes.Contains(code),
            exitCode,
            timedOut,
            DateTime.UtcNow - started,
            output,
            error);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (builder.Length < MaximumCapturedCharacters)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, MaximumCapturedCharacters - builder.Length)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private static async Task<string> AwaitOutputAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
