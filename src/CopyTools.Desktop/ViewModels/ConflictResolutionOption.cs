using CopyTools.Domain;

namespace CopyTools.Desktop.ViewModels;

public sealed record ConflictResolutionOption(ConflictResolutionPolicy Value, string Name);

public sealed record ReparsePointOption(ReparsePointPolicy Value, string Name);
