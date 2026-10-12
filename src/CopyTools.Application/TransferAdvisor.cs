namespace CopyTools.Application;

public sealed record TransferAdvice(
    bool UseDelta,
    bool Compress,
    int MaxParallel,
    long? SpeedLimitBytesPerSecond,
    string Reason);

/// <summary>根据文件规模与目标类型，建议块级增量、压缩、并发与限速组合。</summary>
public sealed class TransferAdvisor
{
    private const long LargeFileThreshold = 32L * 1024 * 1024;

    public TransferAdvice Advise(long fileSize, bool isRemoteTarget, bool isManySmallFiles)
    {
        var useDelta = fileSize >= LargeFileThreshold && isRemoteTarget;
        var compress = isRemoteTarget && !isManySmallFiles;
        var parallel = isManySmallFiles ? 4 : 1;
        var reason = $"{(fileSize >= LargeFileThreshold ? "大文件" : "常规文件")}、"
            + $"{(isRemoteTarget ? "远端目标" : "本地目标")}、"
            + $"{(isManySmallFiles ? "多小文件" : "少量文件")}";
        return new TransferAdvice(
            useDelta,
            compress,
            parallel,
            isRemoteTarget ? 10L * 1024 * 1024 : null,
            reason);
    }
}
