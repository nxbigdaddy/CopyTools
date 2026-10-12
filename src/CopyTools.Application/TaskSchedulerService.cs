using CopyTools.Domain;

namespace CopyTools.Application;

public sealed class TaskSchedulerService : IAsyncDisposable
{
    private readonly ICopyToolsRepository _repository;
    private readonly ScheduledTaskRunner _runner;
    private readonly ScheduleCalculator _calculator;
    private readonly TaskChainRunner _chainRunner;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;

    public TaskSchedulerService(
        ICopyToolsRepository repository,
        ScheduledTaskRunner runner,
        ScheduleCalculator? calculator = null,
        TimeSpan? pollInterval = null,
        TaskChainRunner? chainRunner = null)
    {
        _repository = repository;
        _runner = runner;
        _calculator = calculator ?? new ScheduleCalculator();
        _chainRunner = chainRunner ?? new TaskChainRunner(repository, runner);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
    }

    public event EventHandler<ScheduledRunResult>? RunCompleted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
        {
            return;
        }

        await ArmSchedulesAsync(DateTime.UtcNow, cancellationToken);
        _loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_loopCancellation.Token);
    }

    public async Task<IReadOnlyList<ScheduledRunResult>> ProcessDueAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await _tickGate.WaitAsync(cancellationToken);
        try
        {
            var results = new List<ScheduledRunResult>();
            var schedules = await _repository.GetSchedulesAsync(cancellationToken);
            foreach (var schedule in schedules.Where(item => _calculator.IsDue(item, nowUtc)))
            {
                ScheduledRunResult? result = null;
                if (schedule.MissedRunPolicy == MissedRunPolicy.RunImmediately)
                {
                    result = await _runner.RunAsync(schedule, cancellationToken);
                    results.Add(result);
                    RunCompleted?.Invoke(this, result);
                    if (result.Executed && result.ExecutionResult?.IsSuccessful == true)
                    {
                        await _chainRunner.RunNextAsync(result.TaskId, succeeded: true, cancellationToken);
                    }
                }

                var completedSchedule = schedule with
                {
                    LastTriggeredAtUtc = nowUtc,
                    NextRunAtUtc = schedule.Trigger == ScheduleTrigger.ApplicationStartup
                        ? null
                        : _calculator.CalculateNextUtc(
                            schedule with { LastTriggeredAtUtc = nowUtc },
                            nowUtc),
                };
                await _repository.SaveScheduleAsync(completedSchedule, cancellationToken);
            }

            return results;
        }
        finally
        {
            _tickGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopCancellation is null)
        {
            return;
        }

        await _loopCancellation.CancelAsync();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopCancellation.Dispose();
        _tickGate.Dispose();
    }

    private async Task ArmSchedulesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var schedules = await _repository.GetSchedulesAsync(cancellationToken);
        foreach (var schedule in schedules.Where(item => item.Enabled && item.NextRunAtUtc is null))
        {
            var next = schedule.Trigger == ScheduleTrigger.ApplicationStartup
                ? nowUtc
                : _calculator.CalculateNextUtc(schedule, nowUtc);
            await _repository.SaveScheduleAsync(schedule with { NextRunAtUtc = next }, cancellationToken);
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        await ProcessDueAsync(DateTime.UtcNow, cancellationToken);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await ProcessDueAsync(DateTime.UtcNow, cancellationToken);
        }
    }
}
