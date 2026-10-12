namespace CopyTools.Domain;

public sealed record CopyItemFailure(
    string RelativePath,
    string ErrorMessage,
    int Attempts = 1);
