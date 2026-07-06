using System.Text.Json;

namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Handles tool execution results specifically for the 'compile_chart' tool.
/// </summary>
public sealed class CompileChartToolHandler : BaseChartToolHandler
{
    public override bool CanHandle(string toolName) => toolName == "compile_chart";

    protected override Task<(JsonElement CompiledSpec, string? AppHtml)> ProcessResultAsync(JsonElement flintSpec, object? result, string prompt)
    {
        if (result is string resultStr)
        {
            try
            {
                using var resultDoc = JsonDocument.Parse(resultStr);
                return Task.FromResult<(JsonElement, string?)>((resultDoc.RootElement.Clone(), null));
            }
            catch
            {
                return Task.FromResult<(JsonElement, string?)>((JsonSerializer.SerializeToElement(result), null));
            }
        }
        return Task.FromResult<(JsonElement, string?)>((JsonSerializer.SerializeToElement(result), null));
    }
}
