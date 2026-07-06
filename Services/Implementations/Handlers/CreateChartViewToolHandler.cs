using System;
using System.Text.Json;
using System.Threading.Tasks;
using FlintChartAgent.Services.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Handles tool execution results specifically for the 'create_chart_view' tool.
/// </summary>
public sealed class CreateChartViewToolHandler(IMcpService mcpService) : BaseChartToolHandler
{
    private readonly IMcpService _mcpService = mcpService;

    public override bool CanHandle(string toolName) => toolName == "create_chart_view";

    protected override async Task<JsonElement> ProcessResultAsync(JsonElement flintSpec, object? result, string prompt)
    {
        if (_mcpService.Client == null)
        {
            Console.WriteLine("[FLINT DEBUG] CreateChartViewToolHandler: MCP client is not connected. Returning raw Flint spec.");
            return flintSpec;
        }

        try
        {
            Console.WriteLine("[FLINT DEBUG] CreateChartViewToolHandler: Calling compile_chart tool programmatically to fetch native Vega-Lite spec...");
            var argsDict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object?>>(flintSpec.GetRawText());
            if (argsDict != null)
            {
                // Ensure backend is set to vegalite
                argsDict["backend"] = "vegalite";

                var callResult = await _mcpService.Client.CallToolAsync("compile_chart", argsDict);
                if (callResult.Content != null && callResult.Content.Count > 0)
                {
                    if (callResult.Content[0] is TextContentBlock textBlock)
                    {
                        var textContent = textBlock.Text;
                        Console.WriteLine($"[FLINT DEBUG] CreateChartViewToolHandler: Received compiled spec text from MCP server: {textContent.Substring(0, Math.Min(100, textContent.Length))}...");
                        
                        using var doc = JsonDocument.Parse(textContent);
                        if (doc.RootElement.TryGetProperty("spec", out var specProp))
                        {
                            Console.WriteLine("[FLINT DEBUG] CreateChartViewToolHandler: Found nested 'spec' property. Extracting it.");
                            return specProp.Clone();
                        }
                        
                        return doc.RootElement.Clone();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[FLINT DEBUG] CreateChartViewToolHandler: Exception during compile_chart MCP tool call: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("[FLINT DEBUG] CreateChartViewToolHandler: Fallback, returning raw Flint spec.");
        return flintSpec;
    }
}
