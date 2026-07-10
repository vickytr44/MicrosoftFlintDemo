namespace FlintChartAgent.Configuration;

/// <summary>
/// Strongly-typed settings for the LLM provider (Groq, OpenAI-compatible).
/// </summary>
public sealed class LlmSettings
{
    public const string SectionName = "Llm";

    /// <summary>Active provider name (e.g. "Groq", "GoogleAiStudio"). Defaults to "Groq".</summary>
    public string Provider { get; set; } = "Groq";

    /// <summary>API key for the LLM provider (flat level for backward compatibility).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model identifier (flat level for backward compatibility).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Base URL of the OpenAI-compatible API endpoint (flat level for backward compatibility).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Groq provider settings.</summary>
    public ProviderSettings Groq { get; set; } = new()
    {
        Model = "llama-3.3-70b-versatile",
        Endpoint = "https://api.groq.com/openai/v1"
    };

    /// <summary>Google AI Studio provider settings.</summary>
    public ProviderSettings GoogleAiStudio { get; set; } = new()
    {
        Model = "gemini-2.5-flash",
        Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/"
    };

    /// <summary>
    /// Returns the resolved settings for the currently active provider,
    /// merging nested provider settings with top-level overrides for backward compatibility.
    /// </summary>
    public ProviderSettings GetActiveSettings()
    {
        bool isGoogle = Provider.Equals("GoogleAiStudio", StringComparison.OrdinalIgnoreCase);
        var baseProviderSettings = isGoogle ? GoogleAiStudio : Groq;

        // Merge logic: Use specific provider settings.
        // Fall back to top-level settings if provider settings are empty or placeholders,
        // but only for the Groq provider to prevent Groq keys (e.g. from appsettings.development.json)
        // from overriding Google AI Studio.
        var finalApiKey = (string.IsNullOrWhiteSpace(baseProviderSettings.ApiKey) || IsPlaceholder(baseProviderSettings.ApiKey))
            ? (Provider.Equals("Groq", StringComparison.OrdinalIgnoreCase) ? ApiKey : baseProviderSettings.ApiKey)
            : baseProviderSettings.ApiKey;

        var finalModel = string.IsNullOrWhiteSpace(baseProviderSettings.Model)
            ? (string.IsNullOrWhiteSpace(Model) ? baseProviderSettings.Model : Model)
            : baseProviderSettings.Model;

        var finalEndpoint = string.IsNullOrWhiteSpace(baseProviderSettings.Endpoint)
            ? (string.IsNullOrWhiteSpace(Endpoint) ? baseProviderSettings.Endpoint : Endpoint)
            : baseProviderSettings.Endpoint;

        return new ProviderSettings
        {
            ApiKey = finalApiKey,
            Model = finalModel,
            Endpoint = finalEndpoint
        };
    }

    private static bool IsPlaceholder(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;
        var trimmed = key.Trim();
        return trimmed.Equals("YOUR_GROQ_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("YOUR_GEMINI_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Settings for a single LLM provider.
/// </summary>
public sealed class ProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}
