namespace CopyTools.Domain;

public enum ConflictResolutionPolicy
{
    KeepBoth,
    SourceWins,
    TargetWins,
    Ask,
}

public enum ConflictOrigin
{
    None,
    Source,
    Target,
}
