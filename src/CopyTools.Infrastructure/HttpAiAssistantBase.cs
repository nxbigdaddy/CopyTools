using System.Text;
using System.Text.Json;
using CopyTools.Application;

namespace CopyTools.Infrastructure;

/// <summary>基于 HTTP 的 AI 助手基类，承载客户端生命周期与 JSON 提取逻辑。</summary>
public abstract class HttpAiAssistantBase : IAiAssistant, IDisposable
{
    private readonly HttpClient _client;
    private readonly Uri _base;
    private readonly string _model;
    private readonly bool _ownsClient;

    protected HttpAiAssistantBase(string endpoint, string model, HttpClient? client)
    {
        _base = new Uri(endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/");
        _model = model;
        if (client is null)
        {
            _client = new HttpClient();
            _ownsClient = true;
        }
        else
        {
            _client = client;
            _ownsClient = false;
        }
    }

    public abstract string ProviderName { get; }

    public bool IsAvailable => true;

    protected string Model => _model;

    protected HttpClient Client => _client;

    protected Uri Resolve(string relativePath) => new(_base, relativePath);

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default) =>
        await SendChatAsync(systemPrompt, userPrompt, jsonMode: false, cancellationToken);

    public async Task<T?> CompleteJsonAsync<T>(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var text = await SendChatAsync(
            systemPrompt,
            userPrompt + "\n请仅返回 JSON，不要包含任何解释或代码块围栏。",
            jsonMode: true,
            cancellationToken);
        return AiJson.Parse<T>(text);
    }

    protected abstract Task<string> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        bool jsonMode,
        CancellationToken cancellationToken);

    protected static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
