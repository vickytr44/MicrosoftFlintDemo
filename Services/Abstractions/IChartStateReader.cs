using FlintChartAgent.Services;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Defines read-only contract for retrieving the list of charts and subscribing to chart-added events.
/// </summary>
public interface IChartStateReader
{
    IReadOnlyCollection<ChartData> GetCharts();
    event Action<ChartData>? OnChartAdded;
}
