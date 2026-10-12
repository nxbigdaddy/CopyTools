using CopyTools.Application;
using CopyTools.Domain;

namespace CopyTools.Infrastructure;

/// <summary>根据配置创建 AI 助手；无配置时回退到规则实现。</summary>
public sealed class AiAssistantFactory
{
    public IAiAssistant Create(AiProviderConfig config, HttpClient? client = null)
    {
        return config.Kind switch
        {
            AiProviderKind.Ollama => new OllamaAssistant(
                RequireEndpoint(config.Endpoint),
                string.IsNullOrWhiteSpace(config.Model) ? "llama3" : config.Model,
                client),
            AiProviderKind.OpenAiCompatible => new OpenAiCompatibleAssistant(
                RequireEndpoint(config.Endpoint),
                config.Model,
                config.ApiKey,
                client),
            _ => RuleBasedAssistant.Instance,
        };
    }

    public AiProviderConfig FromSettings(ApplicationSettings settings, string? apiKey = null) =>
        new(
            settings.Normalize().AiProvider switch
            {
                "ollama" => AiProviderKind.Ollama,
                "openai" => AiProviderKind.OpenAiCompatible,
                _ => AiProviderKind.Rule,
            },
            settings.AiEndpoint,
            settings.AiModel,
            apiKey ?? string.Empty);

    private static string RequireEndpoint(string endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint)
            ? endpoint
            : throw new InvalidOperationException("AI 提供方缺少端点地址。");
}
