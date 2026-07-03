using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services.Implementations;

/// <summary>
/// Orchestrates chart tool processing by matching the tool call to the appropriate handler.
/// </summary>
public sealed class ChartProcessor : IChartProcessor
{
    private readonly IEnumerable<IChartToolHandler> _handlers;
    private readonly IChartStateWriter _stateWriter;

    public ChartProcessor(IEnumerable<IChartToolHandler> handlers, IChartStateWriter stateWriter)
    {
        _handlers = handlers;
        _stateWriter = stateWriter;
    }

    public bool IsChartTool(string toolName)
    {
        return _handlers.Any(h => h.CanHandle(toolName));
    }

    public void ProcessToolCall(string prompt, FunctionCallContent call, object? result)
    {
        var handler = _handlers.FirstOrDefault(h => h.CanHandle(call.Name));
        if (handler is not null)
        {
            handler.Process(prompt, call, result, _stateWriter);
        }
    }
}
