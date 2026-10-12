namespace CopyTools.Domain;

public sealed record ControlledCommandResult(
    bool Successful,
    int? ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    string StandardOutput,
    string StandardError);
