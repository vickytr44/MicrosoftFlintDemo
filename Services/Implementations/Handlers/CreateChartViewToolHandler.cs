namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Handles tool execution results specifically for the 'create_chart_view' tool.
/// </summary>
public sealed class CreateChartViewToolHandler : BaseChartToolHandler
{
    public override bool CanHandle(string toolName) => toolName == "create_chart_view";
}
