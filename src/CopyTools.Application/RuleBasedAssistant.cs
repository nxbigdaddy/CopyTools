namespace CopyTools.Application;

/// <summary>规则降级实现：不可用，调用即抛异常，供功能服务捕获后回退到确定性路径。</summary>
public sealed class RuleBasedAssistant : IAiAssistant
{
    public static RuleBasedAssistant Instance { get; } = new();

    public string ProviderName => "rule";

    public bool IsAvailable => false;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("未配置 AI 提供方，已回退到规则处理。");

    public Task<T?> CompleteJsonAsync<T>(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("未配置 AI 提供方，已回退到规则处理。");
}
