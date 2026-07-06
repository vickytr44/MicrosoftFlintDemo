using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Orchestrates the matching and execution of tool handlers for intercepted chat completions.
/// </summary>
public interface IChartProcessor
{
    /// <summary>
    /// Checks if a tool name matches any of the registered chart tools.
    /// </summary>
    bool IsChartTool(string toolName);

    /// <summary>
    /// Processes the function call and its result, pushing updates to the chart state.
    /// </summary>
    Task ProcessToolCallAsync(string prompt, FunctionCallContent call, object? result);
}
