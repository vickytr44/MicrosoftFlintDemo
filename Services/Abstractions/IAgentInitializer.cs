using Microsoft.Agents.AI;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Defines the contract for initializing the AI agent, establishing MCP connections,
/// and returning the created agent instance.
/// </summary>
public interface IAgentInitializer
{
    /// <summary>
    /// Executes the startup initialization sequence and returns the constructed AIAgent.
    /// </summary>
    Task<AIAgent> InitializeAsync(CancellationToken cancellationToken = default);
}
