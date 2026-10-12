namespace CopyTools.Domain;

public sealed record FileEntry(
    string RelativePath,
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc)
{
    public FileAttributes Attributes { get; init; } = FileAttributes.Normal;
    public string? ContentHash { get; init; }
}
