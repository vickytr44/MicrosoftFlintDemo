namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Combines read-only and write-only chart state management contracts.
/// </summary>
public interface IChartStateManager : IChartStateReader, IChartStateWriter
{
}
