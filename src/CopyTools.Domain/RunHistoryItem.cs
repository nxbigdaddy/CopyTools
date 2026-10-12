namespace CopyTools.Domain;

public sealed record RunHistoryItem(
    string RelativePath,
    CopyAction Action,
    string Result,
    long Bytes,
    string? ErrorMessage,
    int Attempts);
