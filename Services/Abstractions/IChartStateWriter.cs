using System.Text.Json;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Defines write-only contract for adding or updating chart specifications.
/// </summary>
public interface IChartStateWriter
{
    void AddChart(string prompt, JsonElement flintSpec, JsonElement compiledSpec, string backend, string? appHtml = null);
}
