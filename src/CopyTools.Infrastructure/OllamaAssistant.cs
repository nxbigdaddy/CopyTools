using System.Text.Json;
using CopyTools.Application;

namespace CopyTools.Infrastructure;

/// <summary>本地 Ollama 助手，走 /api/chat 的 chat 端点。</summary>
public sealed class OllamaAssistant : HttpAiAssistantBase
{
    public OllamaAssistant(string endpoint, string model = "llama3", HttpClient? client = null)
        : base(endpoint, model, client)
    {
    }

    public override string ProviderName => "ollama";

    protected override async Task<string> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = Model,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Resolve("api/chat"))
        {
            Content = JsonContent(payload),
        };
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}
