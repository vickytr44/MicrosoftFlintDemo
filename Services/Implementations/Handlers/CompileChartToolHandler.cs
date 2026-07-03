using System.Text.Json;

namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Handles tool execution results specifically for the 'compile_chart' tool.
/// </summary>
public sealed class CompileChartToolHandler : BaseChartToolHandler
{
    public override bool CanHandle(string toolName) => toolName == "compile_chart";

    protected override JsonElement ProcessResult(JsonElement flintSpec, object? result)
    {
        if (result is string resultStr)
        {
            try
            {
                using var resultDoc = JsonDocument.Parse(resultStr);
                return resultDoc.RootElement.Clone();
            }
            catch
            {
                return JsonSerializer.SerializeToElement(result);
            }
        }
        return JsonSerializer.SerializeToElement(result);
    }
}
