using System.ClientModel;
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
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_llmSettings.Endpoint)
        };

        var openAiClient = new OpenAIClient(credential, clientOptions);

        return openAiClient
            .GetChatClient(_llmSettings.Model)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: "FlintChartAgent", configure: options =>
            {
                options.EnableSensitiveData = true;
            })
            .UseLogging(loggerFactory)
            .UseFunctionInvocation(loggerFactory)
            .Use(inner => new ChartInterceptingChatClient(inner, chartProcessor, chartStateManager))
            .Build();
    }
}
