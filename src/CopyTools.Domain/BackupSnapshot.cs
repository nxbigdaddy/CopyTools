namespace CopyTools.Domain;

public sealed record BackupSnapshot(
    Guid Id,
    DateTime CreatedAtUtc,
    string SourceDirectory,
    BackupMode Mode,
    IReadOnlyList<BackupFileRecord> Files,
    DateTime? ImmutableUntilUtc = null);
