namespace CopyTools.Engine;

/// <summary>扫描阶段计算内容哈希的粒度。</summary>
public enum ContentHashMode
{
    None,
    Sha256,
    SampledSha256,
}
