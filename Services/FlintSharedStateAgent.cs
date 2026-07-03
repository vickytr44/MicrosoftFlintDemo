using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services;

/// <summary>
/// Positional record representing a single chart data snapshot for serialization.
/// </summary>
public record ChartDataSnapshot(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("flintSpec")] string FlintSpec,
    [property: JsonPropertyName("compiledSpec")] string CompiledSpec,
    [property: JsonPropertyName("backend")] string Backend = "vegalite"
);

/// <summary>
/// Positional record representing the aggregate state snapshot of charts.
/// </summary>
public record FlintStateSnapshot(
    [property: JsonPropertyName("charts")] List<ChartDataSnapshot> Charts
);

internal sealed class FlintSharedStateAgent(
    AIAgent innerAgent,
    IChartStateReader stateManager) : DelegatingAIAgent(innerAgent)
{
    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        return RunStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Run the standard agent loop (which lets LLM use tools freely, no JSON mode constraints)
        await foreach (var update in InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        // 2. If it is an AG-UI run, push the updated state snapshot from our local ChartStateManager
        if (options is ChatClientAgentRunOptions { ChatOptions.AdditionalProperties: { } properties } &&
            properties.ContainsKey("ag_ui_state"))
        {
            var currentCharts = stateManager.GetCharts();

            var snapshot = new FlintStateSnapshot(
                currentCharts.Select(c => new ChartDataSnapshot(
                    c.Id,
                    c.Timestamp.ToString("o"),
                    c.Prompt,
                    c.FlintSpec.GetRawText(),
                    c.CompiledSpec.GetRawText(),
                    c.Backend
                )).ToList()
            );

            byte[] stateBytes = JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                FlintAgentSerializerContext.Default.FlintStateSnapshot);

            yield return new AgentResponseUpdate
            {
                Contents = [new DataContent(stateBytes, "application/json")]
            };
        }
    }
}
