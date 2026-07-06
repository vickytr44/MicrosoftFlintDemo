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

    public async Task ProcessAsync(string prompt, FunctionCallContent call, object? result, IChartStateWriter stateWriter)
    {
        try
        {
            Console.WriteLine($"[FLINT DEBUG] BaseChartToolHandler.ProcessAsync: Entered for tool '{call.Name}'. Prompt: '{prompt}'");
            var argsJson = JsonSerializer.Serialize(call.Arguments);
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine("[FLINT DEBUG] BaseChartToolHandler.ProcessAsync: Root arguments are not a JSON object.");
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
                Console.WriteLine("[FLINT DEBUG] BaseChartToolHandler.ProcessAsync: Spec/target is not a JSON object.");
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
            var (compiledSpecJson, appHtml) = await ProcessResultAsync(flintSpecJson, result, prompt);

            // Mutate compiled spec to inject zoom/pan params if it is a supported continuous chart type
            string chartType = "";
            if (flintSpecJson.TryGetProperty("chart_spec", out var chartSpecProp) &&
                chartSpecProp.TryGetProperty("chartType", out var typeProp))
            {
                chartType = typeProp.GetString() ?? "";
            }
            
            bool supportsZoom = chartType == "Line Chart" || chartType == "Scatter Plot" || chartType == "Area Chart" || chartType == "Sparkline";
            if (supportsZoom)
            {
                try
                {
                    var compiledSpecStr = compiledSpecJson.GetRawText();
                    using var tempDoc = JsonDocument.Parse(compiledSpecStr);
                    var targetElement = tempDoc.RootElement;
                    bool hasSpecWrapper = targetElement.TryGetProperty("spec", out var nestedSpec);
                    var specToMutate = hasSpecWrapper ? nestedSpec : targetElement;
                    
                    if (specToMutate.ValueKind == JsonValueKind.Object && !specToMutate.TryGetProperty("params", out _))
                    {
                        var specNode = System.Text.Json.Nodes.JsonNode.Parse(specToMutate.GetRawText());
                        if (specNode is System.Text.Json.Nodes.JsonObject specObjMutated)
                        {
                            Console.WriteLine($"[FLINT DEBUG] BaseChartToolHandler: Injecting zoom/pan params for chart type '{chartType}'");
                            var paramsArray = new System.Text.Json.Nodes.JsonArray
                            {
                                new System.Text.Json.Nodes.JsonObject
                                {
                                    ["name"] = "grid",
                                    ["select"] = "interval",
                                    ["bind"] = "scales"
                                }
                            };
                            specObjMutated["params"] = paramsArray;
                            
                            if (hasSpecWrapper)
                            {
                                var outerNode = System.Text.Json.Nodes.JsonNode.Parse(compiledSpecStr) as System.Text.Json.Nodes.JsonObject;
                                if (outerNode != null)
                                {
                                    outerNode["spec"] = specObjMutated;
                                    using var mutatedDoc = JsonDocument.Parse(outerNode.ToJsonString());
                                    compiledSpecJson = mutatedDoc.RootElement.Clone();
                                }
                            }
                            else
                            {
                                using var mutatedDoc = JsonDocument.Parse(specObjMutated.ToJsonString());
                                compiledSpecJson = mutatedDoc.RootElement.Clone();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FLINT DEBUG] BaseChartToolHandler: Zoom/pan injection failed: {ex.Message}");
                }
            }

            Console.WriteLine($"[FLINT DEBUG] BaseChartToolHandler.ProcessAsync: Calling stateWriter.AddChart for backend: {backend}");
            stateWriter.AddChart(prompt, flintSpecJson, compiledSpecJson, backend, appHtml);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠️ Warning: {GetType().Name} failed to parse tool call: {ex.Message}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Processes the tool execution result and returns the compiled chart specification and optional app UI HTML.
    /// </summary>
    protected virtual Task<(JsonElement CompiledSpec, string? AppHtml)> ProcessResultAsync(JsonElement flintSpec, object? result, string prompt)
    {
        return Task.FromResult<(JsonElement, string?)>((flintSpec, null));
    }
}
