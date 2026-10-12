namespace CopyTools.Engine;

/// <summary>一次块级增量传输的统计结果。</summary>
public sealed record DeltaTransferResult(
    long BlockSizeBytes,
    long ReusedBytes,
    long ChangedBytes)
{
    public long TotalBytes => ReusedBytes + ChangedBytes;

    public double ChangeRatio => TotalBytes == 0 ? 0 : ChangedBytes / (double)TotalBytes;
}
