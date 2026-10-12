namespace CopyTools.Domain;

/// <summary>决定覆盖已有文件时的传输策略。</summary>
public enum TransferMode
{
    /// <summary>整文件复制，与现有行为一致。</summary>
    Full,

    /// <summary>按块比较源与目标，仅写入发生变化的块。</summary>
    Delta,
}
