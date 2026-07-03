using System.Runtime.CompilerServices;
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

        InterceptToolResults(messages);

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

        InterceptToolResults(messages);

        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    private void InterceptToolResults(IEnumerable<ChatMessage> messages)
    {
        var messageList = messages.ToList();
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

                                    chartProcessor.ProcessToolCall(lastUserPrompt, call, result.Result);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
