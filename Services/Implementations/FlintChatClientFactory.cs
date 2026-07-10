using System.ClientModel;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using FlintChartAgent.Configuration;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services.Implementations;

/// <summary>
/// Factory that creates and configures the Flint Chart Agent's IChatClient.
/// </summary>
public sealed class FlintChatClientFactory(
    IOptions<LlmSettings> llmSettings,
    ApiKeyCredential credential,
    ILoggerFactory loggerFactory,
    IChartProcessor chartProcessor,
    ChartStateManager chartStateManager) : IChatClientFactory
{
    private readonly LlmSettings _llmSettings = llmSettings.Value;

    public IChatClient CreateChatClient()
    {
        var activeSettings = _llmSettings.GetActiveSettings();
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(activeSettings.Endpoint)
        };

        var openAiClient = new OpenAIClient(credential, clientOptions);

        var builder = openAiClient
            .GetChatClient(activeSettings.Model)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: "FlintChartAgent", configure: options =>
            {
                options.EnableSensitiveData = true;
            })
            .UseLogging(loggerFactory)
            .UseFunctionInvocation(loggerFactory);

        if (_llmSettings.Provider.Equals("GoogleAiStudio", System.StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.Use(inner => new GeminiToolSanitizingChatClient(inner));
        }

        return builder
            .Use(inner => new ChartInterceptingChatClient(inner, chartProcessor, chartStateManager))
            .Build();
    }
}

internal sealed class GeminiToolSanitizingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SanitizeTools(options);
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            var response = ex.GetRawResponse();
            if (response != null)
            {
                var content = response.Content?.ToString();
                Console.WriteLine($"[GEMINI ERROR DETAIL - RESPONSE BODY]: {content}");
            }
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SanitizeTools(options);
        
        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            var response = ex.GetRawResponse();
            if (response != null)
            {
                var content = response.Content?.ToString();
                Console.WriteLine($"[GEMINI ERROR DETAIL - IMMEDIATE]: {content}");
            }
            throw;
        }

        while (true)
        {
            ChatResponseUpdate update;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                update = enumerator.Current;
            }
            catch (System.ClientModel.ClientResultException ex)
            {
                var response = ex.GetRawResponse();
                if (response != null)
                {
                    var content = response.Content?.ToString();
                    Console.WriteLine($"[GEMINI ERROR DETAIL - IN-STREAM]: {content}");
                }
                throw;
            }
            yield return update;
        }
    }

    private static void SanitizeTools(ChatOptions? options)
    {
        if (options?.Tools is null || options.Tools.Count == 0)
        {
            return;
        }

        var uniqueTools = new List<AIFunction>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in options.Tools)
        {
            if (tool is AIFunction aiFunc)
            {
                if (seenNames.Add(aiFunc.Name))
                {
                    uniqueTools.Add(new GeminiCleanedAIFunction(aiFunc));
                }
            }
        }

        options.Tools.Clear();
        foreach (var tool in uniqueTools)
        {
            options.Tools.Add(tool);
        }
    }
}

internal sealed class GeminiCleanedAIFunction : AIFunction
{
    private readonly AIFunction _inner;
    private readonly JsonElement _jsonSchema;

    public GeminiCleanedAIFunction(AIFunction inner)
    {
        _inner = inner;
        _jsonSchema = CleanSchemaForGemini(inner.JsonSchema);
    }

    public override string Name => _inner.Name;

    public override string Description => _inner.Description;

    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        return await _inner.InvokeAsync(arguments, cancellationToken);
    }

    private static JsonElement CleanSchemaForGemini(JsonElement originalSchema)
    {
        try
        {
            var rawText = originalSchema.GetRawText();
            if (string.IsNullOrWhiteSpace(rawText) || rawText == "{}")
            {
                return originalSchema;
            }

            var node = System.Text.Json.Nodes.JsonNode.Parse(rawText);
            CleanNode(node);

            var jsonText = node?.ToJsonString() ?? "{}";
            using var doc = JsonDocument.Parse(jsonText);
            return doc.RootElement.Clone();
        }
        catch
        {
            return originalSchema;
        }
    }

    private static void CleanNode(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            obj.Remove("additionalProperties");
            obj.Remove("$schema");

            var keys = obj.Select(kv => kv.Key).ToList();
            foreach (var key in keys)
            {
                CleanNode(obj[key]);
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
        {
            foreach (var item in arr)
            {
                CleanNode(item);
            }
        }
    }
}
