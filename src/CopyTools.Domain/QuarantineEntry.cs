namespace CopyTools.Domain;

public sealed record QuarantineEntry(
    Guid Id,
    Guid RunId,
    string OriginalPath,
    string QuarantinePath,
    string RelativePath,
    long Length,
    DateTime QuarantinedAtUtc,
    DateTime? RestoredAtUtc);
