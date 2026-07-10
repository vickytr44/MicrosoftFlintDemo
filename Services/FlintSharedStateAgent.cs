#pragma warning disable MEAI001

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
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
    [property: JsonPropertyName("backend")] string Backend = "vegalite",
    [property: JsonPropertyName("appHtml")] string? AppHtml = null
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
    IList<McpClientTool> mcpTools,
    JsonSerializerOptions? jsonSerializerOptions = null) : DelegatingAIAgent(innerAgent)
{
    private readonly IChartStateReader _stateManager = stateManager;
    private readonly IList<McpClientTool> _mcpTools = mcpTools;
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
            Console.WriteLine($"[FLINT DEBUG] FlintSharedStateAgent: Incoming state contains {incomingSnapshot?.Charts?.Count ?? 0} charts.");
            if (incomingSnapshot?.Charts != null)
            {
                var charts = incomingSnapshot.Charts.Select(c => {
                    JsonElement flintSpecElement;
                    try {
                        using var doc = JsonDocument.Parse(c.FlintSpec);
                        flintSpecElement = doc.RootElement.Clone();
                    } catch {
                        flintSpecElement = default;
                    }

                    JsonElement compiledSpecElement;
                    try {
                        using var doc = JsonDocument.Parse(c.CompiledSpec);
                        compiledSpecElement = doc.RootElement.Clone();
                    } catch {
                        compiledSpecElement = default;
                    }

                    return new ChartData
                    {
                        Id = c.Id,
                        Timestamp = DateTime.TryParse(c.Timestamp, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow,
                        Prompt = c.Prompt,
                        FlintSpec = flintSpecElement,
                        CompiledSpec = compiledSpecElement,
                        Backend = c.Backend,
                        AppHtml = c.AppHtml
                    };
                }).ToList();

                if (_stateManager is ChartStateManager manager)
                {
                    Console.WriteLine($"[FLINT DEBUG] FlintSharedStateAgent: Syncing {charts.Count} charts to Manager");
                    manager.SyncCharts(charts);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FLINT DEBUG] FlintSharedStateAgent: State sync failed: {ex.Message}");
            // Ignore state sync errors on startup
        }

        var firstRunOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = chatRunOptions.ChatOptions.Clone(),
            AllowBackgroundResponses = chatRunOptions.AllowBackgroundResponses,
            ContinuationToken = chatRunOptions.ContinuationToken,
            ChatClientFactory = chatRunOptions.ChatClientFactory,
        };

        if (firstRunOptions.ChatOptions.Tools == null || firstRunOptions.ChatOptions.Tools.Count == 0)
        {
            firstRunOptions.ChatOptions.Tools = [.. _mcpTools];
        }

        // NOTE: We do NOT set ChatOptions.ResponseFormat to ForJsonSchema here because Groq/Llama models 
        // throw an HTTP 400 error when JSON mode is combined with tool/function calling.
        // We instruct the model to speak to the user normally and explain what it is doing,
        // and we manage the co-agent state updates entirely in C# code.
        // To avoid validation errors on strict APIs (like Google AI Studio/Gemini) that reject
        // System messages at the end of the history or require strict User/Assistant alternation,
        // we merge the current chart state and instructions directly into the last User message.
        var lastMessage = messages.LastOrDefault();
        IEnumerable<ChatMessage> finalMessages;

        if (lastMessage != null && lastMessage.Role == ChatRole.User)
        {
            var newContents = new List<AIContent>(lastMessage.Contents);
            newContents.Add(new TextContent("\n\n[System Note - Current Chart State]:"));
            newContents.Add(new TextContent(state.GetRawText()));
            newContents.Add(new TextContent("\nYou can use chart tools (like compile_chart, render_chart, create_chart_view) to generate or update charts as requested by the user. Speak to the user normally to explain what you are doing. Do not output raw JSON state blocks."));

            var updatedLastMessage = new ChatMessage(ChatRole.User, newContents)
            {
                MessageId = lastMessage.MessageId
            };

            finalMessages = messages.SkipLast(1).Append(updatedLastMessage);
        }
        else
        {
            // Fallback: If there's no user message at the end, append a System message.
            ChatMessage stateContextMessage = new(
                ChatRole.System,
                [
                    new TextContent("Here is the current list of charts in JSON format:"),
                    new TextContent(state.GetRawText()),
                    new TextContent("You can use chart tools (like compile_chart, render_chart, create_chart_view) to generate or update charts as requested by the user. Speak to the user normally to explain what you are doing. Do not output raw JSON state blocks.")
                ]);
            finalMessages = messages.Append(stateContextMessage);
        }

        var allUpdates = new List<AgentResponseUpdate>();
        await foreach (var update in InnerAgent.RunStreamingAsync(finalMessages, session, firstRunOptions, cancellationToken).ConfigureAwait(false))
        {
            allUpdates.Add(update);
            yield return update; // Yield all updates to stream chat text live to the user
        }

        // Build and send the updated state snapshot from the local state manager
        var currentCharts = _stateManager.GetCharts();
        Console.WriteLine($"[FLINT DEBUG] FlintSharedStateAgent: Finished LLM run. Current store chart count = {currentCharts.Count}. Serializing snapshot...");
        var snapshot = new FlintStateSnapshot(
            currentCharts.Select(c => new ChartDataSnapshot(
                c.Id,
                c.Timestamp.ToString("o"),
                c.Prompt,
                SafeGetRawText(c.FlintSpec),
                SafeGetRawText(c.CompiledSpec),
                c.Backend,
                c.AppHtml
            )).ToList()
        );

        var stateSnapshot = JsonSerializer.SerializeToElement(snapshot, FlintAgentSerializerContext.Default.FlintStateSnapshot);
        byte[] stateBytes = JsonSerializer.SerializeToUtf8Bytes(
            stateSnapshot,
            _jsonSerializerOptions.GetTypeInfo(typeof(JsonElement)));

        yield return new AgentResponseUpdate
        {
            Contents = [new DataContent(stateBytes, "application/json")]
        };
    }

    private static string SafeGetRawText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return "{}";
        }
        try
        {
            return element.GetRawText();
        }
        catch
        {
            return "{}";
        }
    }
}
