using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace FlintChartAgent.Services;

/// <summary>
/// Provides comparison helpers to check for modifications between agent state snapshots.
/// </summary>
internal static class FlintStateComparer
{
    /// <summary>
    /// Compares two JSON states and returns true if the charts list has changed.
    /// </summary>
    public static bool ChartsChanged(JsonElement oldState, JsonElement newState)
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
