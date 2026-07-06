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

        var firstRunOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = chatRunOptions.ChatOptions.Clone(),
            AllowBackgroundResponses = chatRunOptions.AllowBackgroundResponses,
            ContinuationToken = chatRunOptions.ContinuationToken,
            ChatClientFactory = chatRunOptions.ChatClientFactory,
        };

        // Configure JSON schema response format for structured state output
        firstRunOptions.ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<FlintStateSnapshot>(
            schemaName: "FlintStateSnapshot",
            schemaDescription: "A response containing the current list of charts");

        ChatMessage stateUpdateMessage = new(
            ChatRole.System,
            [
                new TextContent("Here is the current state in JSON format:"),
                new TextContent(state.GetRawText()),
                new TextContent("The new state is:")
            ]);

        var firstRunMessages = messages.Append(stateUpdateMessage);

        var allUpdates = new List<AgentResponseUpdate>();
        await foreach (var update in InnerAgent.RunStreamingAsync(firstRunMessages, session, firstRunOptions, cancellationToken).ConfigureAwait(false))
        {
            allUpdates.Add(update);

            // Yield all non-text updates (tool calls, etc.)
            bool hasNonTextContent = update.Contents.Any(c => c is not TextContent);
            if (hasNonTextContent)
            {
                yield return update;
            }
        }

        var response = allUpdates.ToAgentResponse();

        if (TryDeserialize(response, _jsonSerializerOptions, out JsonElement stateSnapshot))
        {
            byte[] stateBytes = JsonSerializer.SerializeToUtf8Bytes(
                stateSnapshot,
                _jsonSerializerOptions.GetTypeInfo(typeof(JsonElement)));
            yield return new AgentResponseUpdate
            {
                Contents = [new DataContent(stateBytes, "application/json")]
            };
        }
        else
        {
            yield break;
        }

        // ✅ detect whether the state actually changed
        bool stateChanged = ChartsChanged(state, stateSnapshot);

        // ✅ Only narrate if something changed
        var summaryMessage = new ChatMessage(
                ChatRole.System,
                [new TextContent("Please provide a concise summary about the latest change in at most two sentences.")]);

        var secondRunMessages = messages.Concat(response.Messages);

        if (stateChanged)
            secondRunMessages = secondRunMessages.Append(summaryMessage);

        await foreach (var update in InnerAgent.RunStreamingAsync(secondRunMessages, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private static bool TryDeserialize(AgentResponse response, JsonSerializerOptions options, out JsonElement stateSnapshot)
    {
        stateSnapshot = default;

        var textContent = string.Join("", response.Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents)
            .OfType<TextContent>()
            .Select(t => t.Text));

        if (string.IsNullOrWhiteSpace(textContent))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(textContent);
            stateSnapshot = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ChartsChanged(JsonElement oldState, JsonElement newState)
    {
        if (!oldState.TryGetProperty("charts", out var oldCharts) ||
            !newState.TryGetProperty("charts", out var newCharts))
        {
            // If property missing on either side, treat as changed
            return true;
        }

        // Must both be arrays
        if (oldCharts.ValueKind != JsonValueKind.Array ||
            newCharts.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        // Compare array length
        if (oldCharts.GetArrayLength() != newCharts.GetArrayLength())
        {
            return true;
        }

        // Compare elements one by one
        var oldEnum = oldCharts.EnumerateArray();
        var newEnum = newCharts.EnumerateArray();

        using var oldIt = oldEnum.GetEnumerator();
        using var newIt = newEnum.GetEnumerator();

        while (oldIt.MoveNext() && newIt.MoveNext())
        {
            var oldChart = oldIt.Current;
            var newChart = newIt.Current;

            if (!TryGetPropertyString(oldChart, "id", out var oldId) ||
                !TryGetPropertyString(newChart, "id", out var newId) ||
                oldId != newId)
            {
                return true;
            }

            if (!TryGetPropertyString(oldChart, "prompt", out var oldPrompt) ||
                !TryGetPropertyString(newChart, "prompt", out var newPrompt) ||
                oldPrompt != newPrompt)
            {
                return true;
            }

            if (!TryGetPropertyString(oldChart, "backend", out var oldBackend) ||
                !TryGetPropertyString(newChart, "backend", out var newBackend) ||
                oldBackend != newBackend)
            {
                return true;
            }

            oldChart.TryGetProperty("flintSpec", out var oldFlintSpec);
            newChart.TryGetProperty("flintSpec", out var newFlintSpec);
            if (GetRawOrString(oldFlintSpec) != GetRawOrString(newFlintSpec))
            {
                return true;
            }

            oldChart.TryGetProperty("compiledSpec", out var oldCompiledSpec);
            newChart.TryGetProperty("compiledSpec", out var newCompiledSpec);
            if (GetRawOrString(oldCompiledSpec) != GetRawOrString(newCompiledSpec))
            {
                return true;
            }
        }

        return false; // No change
    }

    private static bool TryGetPropertyString(JsonElement element, string propertyName, [NotNullWhen(true)] out string? value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return value != null;
        }
        value = null;
        return false;
    }

    private static string GetRawOrString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }
        return element.GetRawText();
    }
}
