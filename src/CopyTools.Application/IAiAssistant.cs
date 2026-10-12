namespace CopyTools.Application;

/// <summary>AI 助手抽象：生成式文本与结构化 JSON 输出，可由本地或云端实现。</summary>
public interface IAiAssistant
{
    string ProviderName { get; }

    bool IsAvailable { get; }

    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);

    Task<T?> CompleteJsonAsync<T>(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
