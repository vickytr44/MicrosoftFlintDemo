namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Handles tool execution results specifically for the 'render_chart' tool.
/// </summary>
public sealed class RenderChartToolHandler : BaseChartToolHandler
{
    public override bool CanHandle(string toolName) => toolName == "render_chart";
}
