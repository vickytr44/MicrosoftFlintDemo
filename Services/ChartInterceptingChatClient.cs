using System.Text.Json;
using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services;

/// <summary>
/// Intercepts chat completions, checks for Flint Chart MCP tool executions,
/// extracts the chart specifications and results, and pushes them to <see cref="ChartStateManager"/>.
/// </summary>
public sealed class ChartInterceptingChatClient : DelegatingChatClient
{
    private readonly ChartStateManager _stateManager;

    public ChartInterceptingChatClient(IChatClient innerClient, ChartStateManager stateManager)
        : base(innerClient)
    {
        _stateManager = stateManager;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        var messageList = messages.ToList();
        
        // Find the index of the last user prompt to only scan new messages in this turn
        int lastUserIndex = messageList.FindLastIndex(m => m.Role == ChatRole.User);
        
        var lastUserPrompt = lastUserIndex >= 0 ? messageList[lastUserIndex].Text ?? "Generated Chart" : "Generated Chart";

        // Scan only the messages appended after the last user prompt in the current turn
        for (int i = lastUserIndex + 1; i < messageList.Count; i++)
        {
            var message = messageList[i];
            if (message.Role == ChatRole.Assistant)
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionCallContent call && IsChartTool(call.Name))
                    {
                        // Find matching tool result in the messages list (also after lastUserIndex)
                        var resultMessage = messageList.Skip(lastUserIndex + 1).FirstOrDefault(m => m.Role == ChatRole.Tool 
                            && m.Contents.Any(c => c is FunctionResultContent r && r.CallId == call.CallId));
                        
                        var result = resultMessage?.Contents
                            .OfType<FunctionResultContent>()
                            .FirstOrDefault(r => r.CallId == call.CallId);

                        if (result?.Result is not null)
                        {
                            TryProcessChartTool(lastUserPrompt, call, result.Result);
                        }
                    }
                }
            }
        }

        return response;
    }

    private static bool IsChartTool(string name)
        => name == "compile_chart" || name == "render_chart" || name == "create_chart_view";

    private void TryProcessChartTool(string prompt, FunctionCallContent call, object result)
    {
        try
        {
            // Serialize function call arguments
            var argsJson = JsonSerializer.Serialize(call.Arguments);
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // Handle nested 'spec' wrapper (e.g. create_chart_view) or root properties
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
                return;
            }

            // Safe extractions
            target.TryGetProperty("data", out var data);
            target.TryGetProperty("semantic_types", out var semanticTypes);
            target.TryGetProperty("chart_spec", out var chartSpec);
            
            string backend = "vegalite";
            if (root.TryGetProperty("backend", out var backendProp) && backendProp.ValueKind == JsonValueKind.String)
            {
                backend = backendProp.GetString() ?? "vegalite";
            }

            // Reconstruct the full FlintSpec as a JSON element
            var specObj = new Dictionary<string, object?>();
            if (data.ValueKind != JsonValueKind.Undefined) specObj["data"] = data;
            if (semanticTypes.ValueKind != JsonValueKind.Undefined) specObj["semantic_types"] = semanticTypes;
            if (chartSpec.ValueKind != JsonValueKind.Undefined) specObj["chart_spec"] = chartSpec;

            var flintSpecJson = JsonSerializer.SerializeToElement(specObj);

            // Parse result
            JsonElement compiledSpecJson = flintSpecJson;
            if (call.Name == "compile_chart")
            {
                if (result is string resultStr)
                {
                    try
                    {
                        using var resultDoc = JsonDocument.Parse(resultStr);
                        compiledSpecJson = resultDoc.RootElement.Clone();
                    }
                    catch
                    {
                        compiledSpecJson = JsonSerializer.SerializeToElement(result);
                    }
                }
                else
                {
                    compiledSpecJson = JsonSerializer.SerializeToElement(result);
                }
            }

            _stateManager.AddChart(prompt, flintSpecJson, compiledSpecJson, backend);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠️ Warning: Chart interceptor failed to parse tool call: {ex.Message}");
            Console.ResetColor();
        }
    }
}
