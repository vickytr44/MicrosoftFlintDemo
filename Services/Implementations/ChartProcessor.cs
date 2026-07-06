using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services.Implementations;

/// <summary>
/// Orchestrates chart tool processing by matching the tool call to the appropriate handler.
/// </summary>
public sealed class ChartProcessor(IEnumerable<IChartToolHandler> handlers, IChartStateWriter stateWriter) : IChartProcessor
{
    public bool IsChartTool(string toolName)
    {
        return handlers.Any(h => h.CanHandle(toolName));
    }

    public async Task ProcessToolCallAsync(string prompt, FunctionCallContent call, object? result)
    {
        var handler = handlers.FirstOrDefault(h => h.CanHandle(call.Name));
        if (handler is not null)
        {
            await handler.ProcessAsync(prompt, call, result, stateWriter);
        }
    }
}
