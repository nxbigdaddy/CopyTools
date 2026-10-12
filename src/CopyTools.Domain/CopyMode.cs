namespace CopyTools.Domain;

public enum CopyMode
{
    FullCopy,
    IncrementalCopy,
    AccumulateSync,
    MirrorSync,
    MoveSync,
    UpdateSync,
    TwoWaySync,
    VerifyOnly,
    OneWaySync,
}
