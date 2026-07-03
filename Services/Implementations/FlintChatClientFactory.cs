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
public sealed class FlintChatClientFactory : IChatClientFactory
{
    private readonly LlmSettings _llmSettings;
    private readonly ApiKeyCredential _credential;
    private readonly IChartProcessor _chartProcessor;

    public FlintChatClientFactory(
        IOptions<LlmSettings> llmSettings,
        ApiKeyCredential credential,
        IChartProcessor chartProcessor)
    {
        _llmSettings = llmSettings.Value;
        _credential = credential;
        _chartProcessor = chartProcessor;
    }

    public IChatClient CreateChatClient()
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_llmSettings.Endpoint)
        };

        var openAiClient = new OpenAIClient(_credential, clientOptions);

        return openAiClient
            .GetChatClient(_llmSettings.Model)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Use(inner => new ChartInterceptingChatClient(inner, _chartProcessor))
            .Build();
    }
}
