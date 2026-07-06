using System.Text.Json;
using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services.Implementations.Handlers;

/// <summary>
/// Provides shared logic for extracting chart specifications and backends from tool call arguments.
/// </summary>
public abstract class BaseChartToolHandler : IChartToolHandler
{
    public abstract bool CanHandle(string toolName);

    public void Process(string prompt, FunctionCallContent call, object? result, IChartStateWriter stateWriter)
    {
        try
        {
            Console.WriteLine($"[FLINT DEBUG] BaseChartToolHandler.Process: Entered for tool '{call.Name}'. Prompt: '{prompt}'");
            var argsJson = JsonSerializer.Serialize(call.Arguments);
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine("[FLINT DEBUG] BaseChartToolHandler.Process: Root arguments are not a JSON object.");
                return;
            }

            var target = root;
            if (root.TryGetProperty("spec", out var specWrapper))
            {
                if (specWrapper.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        using var specDoc = JsonDocument.Parse(specWrapper.GetString() ?? "{}");
                        target = specDoc.RootElement.Clone();
                    }
                    catch
                    {
                        target = specWrapper;
                    }
                }
                else
                {
                    target = specWrapper;
                }
            }

            if (target.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine("[FLINT DEBUG] BaseChartToolHandler.Process: Spec/target is not a JSON object.");
                return;
            }

            target.TryGetProperty("data", out var data);
            target.TryGetProperty("semantic_types", out var semanticTypes);
            target.TryGetProperty("chart_spec", out var chartSpec);

            string backend = "vegalite";
            if (root.TryGetProperty("backend", out var backendProp) && backendProp.ValueKind == JsonValueKind.String)
            {
                backend = backendProp.GetString() ?? "vegalite";
            }

            var specObj = new Dictionary<string, object?>();
            if (data.ValueKind != JsonValueKind.Undefined) specObj["data"] = data;
            if (semanticTypes.ValueKind != JsonValueKind.Undefined) specObj["semantic_types"] = semanticTypes;
            if (chartSpec.ValueKind != JsonValueKind.Undefined) specObj["chart_spec"] = chartSpec;

            var flintSpecJson = JsonSerializer.SerializeToElement(specObj);
            var compiledSpecJson = ProcessResult(flintSpecJson, result);

            Console.WriteLine($"[FLINT DEBUG] BaseChartToolHandler.Process: Calling stateWriter.AddChart for backend: {backend}");
            stateWriter.AddChart(prompt, flintSpecJson, compiledSpecJson, backend);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠️ Warning: {GetType().Name} failed to parse tool call: {ex.Message}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Processes the tool execution result and returns the compiled chart specification.
    /// </summary>
    protected virtual JsonElement ProcessResult(JsonElement flintSpec, object? result)
    {
        return flintSpec;
    }
}
