namespace CopyTools.Domain;

public sealed record CopyProgress(
    int CompletedFiles,
    int TotalFiles,
    long CopiedBytes,
    long TotalBytes,
    string CurrentRelativePath,
    string Status,
    long CurrentFileBytes = 0,
    long CurrentFileTotalBytes = 0);
