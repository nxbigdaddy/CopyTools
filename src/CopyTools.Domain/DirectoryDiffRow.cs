namespace CopyTools.Domain;

public enum DiffSide
{
    Identical,
    LeftOnly,
    RightOnly,
    BothChanged,
}

public sealed record DirectoryDiffRow(
    string RelativePath,
    bool IsLeftDirectory,
    bool IsRightDirectory,
    long? LeftLength,
    long? RightLength,
    DiffSide Side);
