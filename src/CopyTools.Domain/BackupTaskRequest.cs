namespace CopyTools.Domain;

public sealed record BackupTaskRequest(
    string SourceDirectory,
    string RepositoryDirectory,
    BackupMode Mode,
    FileFilterOptions? Filters = null,
    VerificationMode VerificationMode = VerificationMode.Sha256,
    string? ManifestSourceDirectory = null);
