using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class AutoTriggerService : IAsyncDisposable
{
    private readonly ICopyToolsRepository _repository;
    private readonly ScheduledTaskRunner _runner;
    private readonly Dictionary<Guid, WatchRegistration> _registrations = [];
    private readonly object _sync = new();
    private readonly SemaphoreSlim _configureGate = new(1, 1);

    public AutoTriggerService(ICopyToolsRepository repository, ScheduledTaskRunner runner)
    {
        _repository = repository;
        _runner = runner;
    }

    public event EventHandler<ScheduledRunResult>? RunCompleted;

    public async Task ConfigureAsync(
        IReadOnlyList<AutoTriggerRule> rules,
        CancellationToken cancellationToken = default)
    {
        await _configureGate.WaitAsync(cancellationToken);
        try
        {
            await DisposeRegistrationsAsync();
            foreach (var rule in rules.Where(rule => rule.Enabled))
            {
                var task = await _repository.GetTaskAsync(rule.TaskId, cancellationToken);
                if (task is null) continue;

                var normalized = rule.Normalize();
                var registration = normalized.Kind switch
                {
                    AutoTriggerKind.FileChange => CreateFileChangeRegistration(task, normalized),
                    AutoTriggerKind.PathAvailable => CreateAvailabilityRegistration(task, normalized),
                    _ => throw new ArgumentOutOfRangeException(nameof(rules)),
                };
                lock (_sync) _registrations[task.Id] = registration;
            }
        }
        finally { _configureGate.Release(); }
    }

    public async ValueTask DisposeAsync() => await DisposeRegistrationsAsync();

    private WatchRegistration CreateFileChangeRegistration(SavedCopyTask task, AutoTriggerRule rule)
    {
        var roots = GetWatcherRoots(task).ToArray();
        if (roots.Any(root => !Directory.Exists(root)))
        {
            return CreateAvailabilityRegistration(task, rule with { Kind = AutoTriggerKind.PathAvailable });
        }

        var registration = new WatchRegistration(rule);
        foreach (var root in roots)
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler changed = (_, _) => Debounce(task, registration);
            RenamedEventHandler renamed = (_, _) => Debounce(task, registration);
            watcher.Changed += changed; watcher.Created += changed; watcher.Deleted += changed; watcher.Renamed += renamed;
            registration.Watchers.Add(watcher);
        }
        return registration;
    }

    private WatchRegistration CreateAvailabilityRegistration(SavedCopyTask task, AutoTriggerRule rule)
    {
        var registration = new WatchRegistration(rule);
        registration.PollTask = Task.Run(async () =>
        {
            var wasAvailable = PathsAvailable(task);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(registration.Cancellation.Token))
            {
                var available = PathsAvailable(task);
                if (available && !wasAvailable)
                {
                    Debounce(task, registration);
                }

                wasAvailable = available;
            }
        }, registration.Cancellation.Token);
        return registration;
    }

    private void Debounce(SavedCopyTask task, WatchRegistration registration)
    {
        lock (registration)
        {
            registration.DebounceCancellation?.Cancel();
            registration.DebounceCancellation?.Dispose();
            registration.DebounceCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                registration.Cancellation.Token);
            var token = registration.DebounceCancellation.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(registration.Rule.DebounceDelay, token);
                    if (!ShouldTrigger(registration))
                    {
                        return;
                    }

                    var result = await _runner.RunAsync(new TaskSchedule(
                        task.Id,
                        true,
                        ScheduleTrigger.ApplicationStartup,
                        TimeSpan.Zero,
                        AllowOverwrite: registration.Rule.AllowOverwrite,
                        AllowQuarantine: registration.Rule.AllowQuarantine), token);
                    lock (registration)
                    {
                        registration.LastRunAtUtc = DateTime.UtcNow;
                    }

                    RunCompleted?.Invoke(this, result);
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }
    }

    private async Task DisposeRegistrationsAsync()
    {
        WatchRegistration[] registrations;
        lock (_sync)
        {
            registrations = _registrations.Values.ToArray();
            _registrations.Clear();
        }

        foreach (var registration in registrations)
        {
            registration.Cancellation.Cancel();
            registration.DebounceCancellation?.Cancel();
            foreach (var watcher in registration.Watchers) watcher.Dispose();
            if (registration.PollTask is not null)
            {
                try
                {
                    await registration.PollTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            registration.DebounceCancellation?.Dispose();
            registration.Cancellation.Dispose();
        }
    }

    private static bool ShouldTrigger(WatchRegistration registration)
    {
        var minimumInterval = registration.Rule.MinIntervalBetweenRuns;
        if (minimumInterval is null)
        {
            return true;
        }

        DateTime? lastRun;
        lock (registration)
        {
            lastRun = registration.LastRunAtUtc;
        }

        return lastRun is null || DateTime.UtcNow - lastRun.Value >= minimumInterval;
    }

    private static bool PathsAvailable(SavedCopyTask task) =>
        task.Request.EffectiveSourcePaths.All(path => File.Exists(path) || Directory.Exists(path))
        && (Directory.Exists(task.Request.TargetDirectory)
            || Directory.Exists(Path.GetDirectoryName(task.Request.TargetDirectory)));

    private static IEnumerable<string> GetWatcherRoots(SavedCopyTask task)
    {
        var paths = task.Request.EffectiveSourcePaths.AsEnumerable();
        if (task.Request.Mode == CopyMode.TwoWaySync) paths = paths.Append(task.Request.TargetDirectory);
        return paths.Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path)
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WatchRegistration(AutoTriggerRule rule)
    {
        public AutoTriggerRule Rule { get; } = rule;
        public List<FileSystemWatcher> Watchers { get; } = [];
        public Task? PollTask { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public CancellationTokenSource? DebounceCancellation { get; set; }
        public DateTime? LastRunAtUtc { get; set; }
    }
}
