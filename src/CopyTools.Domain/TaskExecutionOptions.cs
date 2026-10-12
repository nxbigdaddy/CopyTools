namespace CopyTools.Domain;

public sealed record TaskExecutionOptions(
    Guid TaskId,
    string Description = "",
    string Group = "",
    IReadOnlyList<string>? Tags = null,
    int Priority = 0,
    IReadOnlyList<Guid>? Dependencies = null,
    ControlledCommand? BeforeCommand = null,
    ControlledCommand? AfterCommand = null,
    bool RunAfterCommandOnFailure = false,
    Guid? NextTaskId = null,
    bool ChainOutputAsSource = false)
{
    public IReadOnlyList<string> EffectiveTags => Tags ?? [];
    public IReadOnlyList<Guid> EffectiveDependencies => Dependencies ?? [];
    public TaskExecutionOptions Normalize() => this with
    {
        Description = Description.Trim(),
        Group = Group.Trim(),
        Tags = EffectiveTags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray(),
        Priority = Math.Clamp(Priority, -100, 100),
        Dependencies = EffectiveDependencies.Where(id => id != TaskId).Distinct().Take(100).ToArray(),
    };
}
