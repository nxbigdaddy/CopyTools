namespace CopyTools.Domain;

public sealed record QuarantinedFileResult(
    string OriginalPath,
    string QuarantinePath,
    string RelativePath,
    long Length,
    DateTime QuarantinedAtUtc);
