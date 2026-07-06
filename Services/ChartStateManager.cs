using System.Collections.Concurrent;
using System.Text.Json;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services;

public sealed class ChartData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Prompt { get; set; } = string.Empty;
    public JsonElement FlintSpec { get; set; }
    public JsonElement CompiledSpec { get; set; }
    public string Backend { get; set; } = "vegalite";
}

/// <summary>
/// Thread-safe in-memory store for generated charts.
/// Notifies connected SSE clients whenever a new chart is added.
/// </summary>
public sealed class ChartStateManager : IChartStateManager
{
    private readonly ConcurrentQueue<ChartData> _charts = new();
    private const int MaxHistory = 50;

    public event Action<ChartData>? OnChartAdded;

    public IReadOnlyCollection<ChartData> GetCharts() => _charts.ToArray();

    public void AddChart(string prompt, JsonElement flintSpec, JsonElement compiledSpec, string backend)
    {
        // Prevent duplicate specifications for the same prompt
        var chartsList = _charts.ToArray();
        if (chartsList.Length > 0)
        {
            var lastChart = chartsList[^1];
            if (lastChart.Prompt == prompt && 
                JsonSerializer.Serialize(lastChart.FlintSpec) == JsonSerializer.Serialize(flintSpec))
            {
                // If the last one has a raw Flint spec as compiledSpec, and the new one has a compiledSpec, upgrade it
                var isLastSpecRaw = lastChart.CompiledSpec.TryGetProperty("chart_spec", out _);
                var isNewSpecCompiled = !compiledSpec.TryGetProperty("chart_spec", out _);
                
                if (isLastSpecRaw && isNewSpecCompiled)
                {
                    lastChart.CompiledSpec = compiledSpec;
                    lastChart.Backend = backend;
                }
                return;
            }
        }

        var newChart = new ChartData
        {
            Prompt = prompt,
            FlintSpec = flintSpec,
            CompiledSpec = compiledSpec,
            Backend = backend
        };

        _charts.Enqueue(newChart);

        // Keep history bounded
        while (_charts.Count > MaxHistory)
        {
            _charts.TryDequeue(out _);
        }

        OnChartAdded?.Invoke(newChart);
    }

    public void SyncCharts(List<ChartData> charts)
    {
        _charts.Clear();
        foreach (var chart in charts)
        {
            _charts.Enqueue(chart);
        }
    }
}

