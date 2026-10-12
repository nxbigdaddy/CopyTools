namespace CopyTools.Engine;

public sealed class CopyExecutionControl
{
    private readonly object _sync = new();
    private TaskCompletionSource _resumeSignal = CompletedSignal();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_sync)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            _resumeSignal.TrySetResult();
        }
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return IsPaused
                ? _resumeSignal.Task.WaitAsync(cancellationToken)
                : Task.CompletedTask;
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
