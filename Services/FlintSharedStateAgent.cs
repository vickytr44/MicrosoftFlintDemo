#pragma warning disable MEAI001

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
    IChartStateReader stateManager,
    JsonSerializerOptions? jsonSerializerOptions = null) : DelegatingAIAgent(innerAgent)
{
    private readonly IChartStateReader _stateManager = stateManager;
    private readonly JsonSerializerOptions _jsonSerializerOptions = jsonSerializerOptions ?? FlintAgentSerializerContext.Default.Options;

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
        if (options is not ChatClientAgentRunOptions { ChatOptions.AdditionalProperties: { } properties } chatRunOptions ||
            !properties.TryGetValue("ag_ui_state", out JsonElement state))
        {
            await foreach (var update in InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
            yield break;
        }

        // 1. Sync the incoming frontend state to our local ChartStateManager before the agent runs
        try
        {
            var incomingSnapshot = JsonSerializer.Deserialize<FlintStateSnapshot>(state.GetRawText(), _jsonSerializerOptions);
            if (incomingSnapshot?.Charts != null)
            {
                var charts = incomingSnapshot.Charts.Select(c => new ChartData
                {
                    Id = c.Id,
                    Timestamp = DateTime.TryParse(c.Timestamp, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow,
                    Prompt = c.Prompt,
                    FlintSpec = JsonDocument.Parse(c.FlintSpec).RootElement.Clone(),
                    CompiledSpec = JsonDocument.Parse(c.CompiledSpec).RootElement.Clone(),
                    Backend = c.Backend
                }).ToList();

                if (_stateManager is ChartStateManager manager)
                {
                    manager.SyncCharts(charts);
                }
            }
        }
        catch (Exception)
        {
            // Ignore state sync errors on startup
        }

        var firstRunOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = chatRunOptions.ChatOptions.Clone(),
            AllowBackgroundResponses = chatRunOptions.AllowBackgroundResponses,
            ContinuationToken = chatRunOptions.ContinuationToken,
            ChatClientFactory = chatRunOptions.ChatClientFactory,
        };

        // NOTE: We do NOT set ChatOptions.ResponseFormat to ForJsonSchema here because Groq/Llama models 
        // throw an HTTP 400 error when JSON mode is combined with tool/function calling.
        // Instead, we instruct the model via prompt to return raw JSON matching the state schema.
        ChatMessage stateUpdateMessage = new(
            ChatRole.System,
            [
                new TextContent("Here is the current state in JSON format:"),
                new TextContent(state.GetRawText()),
                new TextContent("You must respond with the updated state in JSON format matching this schema:\n" +
                               "{\n" +
                               "  \"charts\": [\n" +
                               "    {\n" +
                               "      \"id\": \"string (guid)\",\n" +
                               "      \"timestamp\": \"string (ISO 8601 UTC timestamp)\",\n" +
                               "      \"prompt\": \"string\",\n" +
                               "      \"flintSpec\": \"string (JSON-serialized Flint spec)\",\n" +
                               "      \"compiledSpec\": \"string (JSON-serialized compiled Vega-Lite spec)\",\n" +
                               "      \"backend\": \"vegalite\"\n" +
                               "    }\n" +
                               "  ]\n" +
                               "}\n" +
                               "Do not output any introductory or explanatory text. Output ONLY the JSON object. If you need to make changes, execute the appropriate tools first, and then return the updated state in JSON.")
            ]);

        var firstRunMessages = messages.Append(stateUpdateMessage);

        var allUpdates = new List<AgentResponseUpdate>();
        await foreach (var update in InnerAgent.RunStreamingAsync(firstRunMessages, session, firstRunOptions, cancellationToken).ConfigureAwait(false))
        {
            allUpdates.Add(update);
            yield return update; // Yield all updates to keep connection alive during LLM generation
        }

        var response = allUpdates.ToAgentResponse();
        JsonElement stateSnapshot;
        bool stateChanged = false;

        if (FlintStateSerializer.TryDeserialize(response, out stateSnapshot))
        {
            byte[] stateBytes = JsonSerializer.SerializeToUtf8Bytes(
                stateSnapshot,
                _jsonSerializerOptions.GetTypeInfo(typeof(JsonElement)));
            yield return new AgentResponseUpdate
            {
                Contents = [new DataContent(stateBytes, "application/json")]
            };

            stateChanged = FlintStateComparer.ChartsChanged(state, stateSnapshot);
        }
        else
        {
            yield break;
        }

        // Skip a second LLM round-trip for narration — build a concise summary
        // directly from the current service state instead. This eliminates the
        // second slow LLM call and prevents BodyTimeoutError on the proxy stream.
        if (stateChanged)
        {
            var currentCharts = _stateManager.GetCharts().ToList();
            string summaryText;
            if (currentCharts.Count > 0)
            {
                var lastChart = currentCharts[^1];
                summaryText = $"I've successfully generated the chart for: \"{lastChart.Prompt}\" using the {lastChart.Backend} backend.";
            }
            else
            {
                summaryText = "The chart state was updated.";
            }

            yield return new AgentResponseUpdate
            {
                Contents = [new TextContent(summaryText)]
            };
        }
    }
}
