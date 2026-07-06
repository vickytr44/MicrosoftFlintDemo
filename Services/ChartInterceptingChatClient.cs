using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services;

/// <summary>
/// Intercepts chat completions, checks for Flint Chart MCP tool executions,
/// and delegates processing to the <see cref="IChartProcessor"/>.
/// </summary>
public sealed class ChartInterceptingChatClient(
    IChatClient innerClient,
    IChartProcessor chartProcessor) : DelegatingChatClient(innerClient)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options is not null && options.ResponseFormat is not null)
        {
            // Clear tools when JSON mode/schema is requested, because some LLM providers (like Groq)
            // do not allow combining response_format with tool/function calling.
            options.Tools = null;
        }

        await InterceptToolResultsAsync(messages);

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options is not null && options.ResponseFormat is not null)
        {
            // Clear tools when JSON mode/schema is requested, because some LLM providers (like Groq)
            // do not allow combining response_format with tool/function calling.
            options.Tools = null;
        }

        await InterceptToolResultsAsync(messages);

        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    private async Task InterceptToolResultsAsync(IEnumerable<ChatMessage> messages)
    {
        var messageList = messages.ToList();
        
        // 1. Process the latest tool execution if it exists
        if (messageList.Count > 0)
        {
            var lastMessage = messageList[^1];
            if (lastMessage.Role == ChatRole.Tool)
            {
                foreach (var content in lastMessage.Contents)
                {
                    if (content is FunctionResultContent result)
                    {
                        // Find the matching function call in the history
                        for (int i = messageList.Count - 2; i >= 0; i--)
                        {
                            var msg = messageList[i];
                            if (msg.Role == ChatRole.Assistant)
                            {
                                var call = msg.Contents
                                    .OfType<FunctionCallContent>()
                                    .FirstOrDefault(c => c.CallId == result.CallId);

                                if (call is not null && chartProcessor.IsChartTool(call.Name))
                                {
                                    // Find the last user prompt
                                    int lastUserIndex = messageList.FindLastIndex(m => m.Role == ChatRole.User);
                                    var lastUserPrompt = lastUserIndex >= 0 ? messageList[lastUserIndex].Text ?? "Generated Chart" : "Generated Chart";

                                    await chartProcessor.ProcessToolCallAsync(lastUserPrompt, call, result.Result);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 2. Sanitize all tool results in the message history to prevent token rate limits (TPM limits)
        foreach (var msg in messageList)
        {
            if (msg.Role == ChatRole.Tool)
            {
                foreach (var content in msg.Contents)
                {
                    if (content is FunctionResultContent result && result.Result is not null)
                    {
                        var resultString = GetResultString(result.Result);
                        if (resultString.Contains("data:image/") || resultString.Contains("base64") || resultString.Length > 1000)
                        {
                            result.Result = "Image generated and saved successfully. [Base64 Image Data Truncated]";
                        }
                    }
                }
            }
        }
    }

    private static string GetResultString(object? result)
    {
        if (result is null) return string.Empty;
        if (result is string str) return str;
        try
        {
            return JsonSerializer.Serialize(result);
        }
        catch
        {
            return result.ToString() ?? string.Empty;
        }
    }
}
