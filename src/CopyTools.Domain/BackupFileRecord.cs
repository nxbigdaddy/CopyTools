namespace CopyTools.Domain;

public sealed record BackupFileRecord(
    string RelativePath,
    long Length,
    DateTime LastWriteTimeUtc,
    FileAttributes Attributes,
    string Sha256,
    string ObjectRelativePath);
