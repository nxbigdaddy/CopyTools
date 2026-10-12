using System.Text.Json;
using CopyTools.Application;

namespace CopyTools.Infrastructure;

/// <summary>OpenAI 兼容云端助手（/chat/completions），带 Bearer 密钥。</summary>
public sealed class OpenAiCompatibleAssistant : HttpAiAssistantBase
{
    private readonly string _apiKey;

    public OpenAiCompatibleAssistant(
        string endpoint,
        string model,
        string apiKey = "",
        HttpClient? client = null)
        : base(endpoint, model, client)
    {
        _apiKey = apiKey;
    }

    public override string ProviderName => "openai";

    protected override async Task<string> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };
        if (jsonMode)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Resolve("chat/completions"))
        {
            Content = JsonContent(payload),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
        }

        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }
}
