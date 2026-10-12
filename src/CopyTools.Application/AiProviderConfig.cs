namespace CopyTools.Application;

public enum AiProviderKind
{
    Rule,
    Ollama,
    OpenAiCompatible,
}

public sealed record AiProviderConfig(
    AiProviderKind Kind = AiProviderKind.Rule,
    string Endpoint = "",
    string Model = "",
    string ApiKey = "");
