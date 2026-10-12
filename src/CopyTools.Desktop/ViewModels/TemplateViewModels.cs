using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed class FilterRuleTemplateViewModel(FilterRuleTemplate template)
{
    public FilterRuleTemplate Template { get; } = template;
    public string Name => Template.Name;
    public string Summary => $"包含 {Template.Filters.EffectiveIncludePatterns.Count}；排除 {Template.Filters.EffectiveExcludePatterns.Count}；目录 {Template.Filters.EffectiveExcludedDirectories.Count}";
}

public sealed class TaskTemplateViewModel(TaskTemplate template)
{
    public TaskTemplate Template { get; } = template;
    public string Name => Template.Name;
    public string Mode => Template.Mode.ToString();
    public string Verification => Template.VerificationMode.ToString();
}

public sealed class DuplicateGroupViewModel(DuplicateFileGroup group)
{
    public DuplicateFileGroup Group { get; } = group;
    public string Hash => Group.Sha256;
    public int Count => Group.Files.Count;
    public string Size => Format(Group.Length);
    public string Reclaimable => Format(Group.ReclaimableBytes);
    public IReadOnlyList<DuplicateFileEntry> Files => Group.Files;
    private static string Format(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.##} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024:0.##} MB" : $"{bytes / 1024d:0.##} KB";
}
