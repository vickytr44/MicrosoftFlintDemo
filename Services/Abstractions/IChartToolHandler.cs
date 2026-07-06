using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Strategy pattern interface for handling specific chart-related tool execution results.
/// </summary>
public interface IChartToolHandler
{
    /// <summary>
    /// Determines whether this handler can process the given tool name.
    /// </summary>
    bool CanHandle(string toolName);

    /// <summary>
    /// Processes the tool invocation and writes the resulting chart data.
    /// </summary>
    Task ProcessAsync(string prompt, FunctionCallContent call, object? result, IChartStateWriter stateWriter);
}
