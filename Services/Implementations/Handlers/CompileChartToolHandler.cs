using System.Text.Json;

namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Handles tool execution results specifically for the 'compile_chart' tool.
/// </summary>
public sealed class CompileChartToolHandler : BaseChartToolHandler
{
    public override bool CanHandle(string toolName) => toolName == "compile_chart";

    protected override Task<JsonElement> ProcessResultAsync(JsonElement flintSpec, object? result)
    {
        if (result is string resultStr)
        {
            try
            {
                using var resultDoc = JsonDocument.Parse(resultStr);
                return Task.FromResult(resultDoc.RootElement.Clone());
            }
            catch
            {
                return Task.FromResult(JsonSerializer.SerializeToElement(result));
            }
        }
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }
}
