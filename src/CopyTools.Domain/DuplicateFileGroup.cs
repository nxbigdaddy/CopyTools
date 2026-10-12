namespace CopyTools.Domain;

public sealed record DuplicateFileEntry(string Path, long Length, DateTime LastWriteTimeUtc);

public sealed record DuplicateFileGroup(string Sha256, long Length, IReadOnlyList<DuplicateFileEntry> Files)
{
    public long ReclaimableBytes => Math.Max(0, Files.Count - 1) * Length;
}
