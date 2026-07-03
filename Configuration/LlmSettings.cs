namespace FlintChartAgent.Configuration;

/// <summary>
/// Strongly-typed settings for the LLM provider (Groq, OpenAI-compatible).
/// </summary>
public sealed class LlmSettings
{
    public const string SectionName = "Llm";

    /// <summary>API key for the LLM provider. Can also be set via LLM__APIKEY env var.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model identifier (e.g., "llama-3.3-70b-versatile", "mixtral-8x7b-32768").</summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>Base URL of the OpenAI-compatible API endpoint.</summary>
    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";
}
