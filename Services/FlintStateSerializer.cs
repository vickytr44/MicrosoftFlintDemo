#pragma warning disable MEAI001

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services;

/// <summary>
/// Provides serialization and deserialization helpers for the Flint agent state snapshots.
/// </summary>
internal static class FlintStateSerializer
{
    /// <summary>
    /// Attempts to extract and parse the JSON state snapshot from an AgentResponse.
    /// </summary>
    public static bool TryDeserialize(AgentResponse response, out JsonElement stateSnapshot)
    {
        stateSnapshot = default;

        // Concatenate all text content from Assistant messages
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
}
