namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Defines the contract for retrieving system prompt instructions.
/// </summary>
public interface IPromptProvider
{
    /// <summary>
    /// Gets the primary system instructions for the Flint Chart Agent.
    /// </summary>
    string SystemPrompt { get; }
}
