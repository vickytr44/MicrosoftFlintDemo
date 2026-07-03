using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services;

public class ChartDataSnapshot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("flintSpec")]
    public string FlintSpec { get; set; } = string.Empty;

    [JsonPropertyName("compiledSpec")]
    public string CompiledSpec { get; set; } = string.Empty;

    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "vegalite";
}

public class FlintStateSnapshot
{
    [JsonPropertyName("charts")]
    public List<ChartDataSnapshot> Charts { get; set; } = [];
}

internal sealed class FlintSharedStateAgent : DelegatingAIAgent
{
    private readonly ChartStateManager _stateManager;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public FlintSharedStateAgent(AIAgent innerAgent, ChartStateManager stateManager, JsonSerializerOptions jsonSerializerOptions)
        : base(innerAgent)
    {
        _stateManager = stateManager;
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        return RunStreamingAsync(messages, thread, options, cancellationToken).ToAgentResponseAsync(cancellationToken);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Run the standard agent loop (which lets LLM use tools freely, no JSON mode constraints)
        await foreach (var update in InnerAgent.RunStreamingAsync(messages, thread, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        // 2. If it is an AG-UI run, push the updated state snapshot from our local ChartStateManager
        if (options is ChatClientAgentRunOptions { ChatOptions.AdditionalProperties: { } properties } &&
            properties.ContainsKey("ag_ui_state"))
        {
            var currentCharts = _stateManager.GetCharts();

            var snapshot = new FlintStateSnapshot
            {
                Charts = currentCharts.Select(c => new ChartDataSnapshot
                {
                    Id = c.Id,
                    Timestamp = c.Timestamp.ToString("o"),
                    Prompt = c.Prompt,
                    FlintSpec = c.FlintSpec.GetRawText(),
                    CompiledSpec = c.CompiledSpec.GetRawText(),
                    Backend = c.Backend
                }).ToList()
            };

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
