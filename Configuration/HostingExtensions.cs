using System.ClientModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using FlintChartAgent.Configuration;
using FlintChartAgent.Services;
using FlintChartAgent.Services.Abstractions;
using FlintChartAgent.Services.Implementations;
using FlintChartAgent.Services.Implementations.Handlers;

namespace FlintChartAgent.Configuration;

/// <summary>
/// Provides extension methods for clean application bootstrapping, service registration, and agent initialization.
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// Configures the application's configuration sources.
    /// </summary>
    public static void ConfigureAppConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    }

    /// <summary>
    /// Registers all required services for the Flint Chart Agent into the DI container.
    /// </summary>
    public static IServiceCollection AddFlintAgentServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind strongly-typed settings from configuration
        services.Configure<LlmSettings>(configuration.GetSection(LlmSettings.SectionName));
        services.Configure<McpSettings>(configuration.GetSection(McpSettings.SectionName));

        // Logging
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Warning);
        });

        // Add CORS for the Next.js frontend
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        // Register AGUI services
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(FlintAgentSerializerContext.Default));
        services.AddAGUI();

        // Prompt Provider
        services.AddSingleton<IPromptProvider, SystemPromptProvider>();

        // MCP service (Flint Chart server connection)
        services.AddSingleton<IMcpService, FlintMcpService>();

        // Chart State Manager (shared singleton)
        services.AddSingleton<ChartStateManager>();
        services.AddSingleton<IChartStateManager>(sp => sp.GetRequiredService<ChartStateManager>());
        services.AddSingleton<IChartStateReader>(sp => sp.GetRequiredService<ChartStateManager>());
        services.AddSingleton<IChartStateWriter>(sp => sp.GetRequiredService<ChartStateManager>());

        // Chart Tool Handlers
        services.AddSingleton<IChartToolHandler, CompileChartToolHandler>();
        services.AddSingleton<IChartToolHandler, RenderChartToolHandler>();
        services.AddSingleton<IChartToolHandler, CreateChartViewToolHandler>();

        // Chart Processor
        services.AddSingleton<IChartProcessor, ChartProcessor>();

        // ApiKeyCredential
        services.AddSingleton<ApiKeyCredential>(sp =>
        {
            var llmSettings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
            var apiKey = llmSettings.ApiKey;

            if (IsPlaceholderOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "LLM API key not configured. Set 'Llm:ApiKey' in appsettings.development.json " +
                    "or the 'Llm__ApiKey' environment variable.");
            }

            return new ApiKeyCredential(apiKey);
        });

        // Chat client — configured for Groq with custom interceptor middleware
        services.AddSingleton<IChatClient>(sp =>
        {
            var llmSettings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
            var credential = sp.GetRequiredService<ApiKeyCredential>();
            var chartProcessor = sp.GetRequiredService<IChartProcessor>();

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(llmSettings.Endpoint)
            };

            var openAiClient = new OpenAIClient(credential, clientOptions);

            return openAiClient
                .GetChatClient(llmSettings.Model)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Use(inner => new ChartInterceptingChatClient(inner, chartProcessor))
                .Build();
        });

        return services;
    }

    /// <summary>
    /// Connects to the MCP server, discovers tools, creates the AI Agent, and maps the AG-UI endpoint.
    /// </summary>
    public static async Task InitializeFlintAgentAsync(this WebApplication app)
    {
        var mcpService = app.Services.GetRequiredService<IMcpService>();
        var chatClient = app.Services.GetRequiredService<IChatClient>();
        var jsonOptions = JsonSerializerOptions.Default;

        Console.WriteLine("⏳ Starting Flint Chart MCP server...");
        var mcpTools = await mcpService.ConnectAsync();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Connected! Found {mcpTools.Count} Flint chart tools:");
        Console.ResetColor();
        foreach (var tool in mcpTools)
        {
            var desc = tool.Description ?? "";
            var truncated = desc.Length > 80 ? desc[..80] + "..." : desc;
            Console.WriteLine($"   🔧 {tool.Name}: {truncated}");
        }

        // System prompt for the Flint Chart Agent
        var promptProvider = app.Services.GetRequiredService<IPromptProvider>();

        // Create the ChatClientAgent with the MCP tools
        var chatClientAgent = new ChatClientAgent(
            chatClient,
            instructions: promptProvider.SystemPrompt,
            name: "FlintChartAgent",
            description: "A data visualization assistant that creates charts using the Flint chart system.",
            tools: [.. mcpTools]);

        // Wrap with shared-state for CopilotKit co-agent state synchronization
        var stateManager = app.Services.GetRequiredService<IChartStateReader>();
        var agent = new FlintSharedStateAgent(chatClientAgent, stateManager, jsonOptions);

        // Map the AG-UI agent endpoint
        app.MapAGUI("/agent", agent);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n🚀 Flint Chart AGUI Server is running at http://localhost:5000/agent");
        Console.WriteLine("   Connect CopilotKit frontend to this endpoint.");
        Console.ResetColor();
    }

    private static bool IsPlaceholderOrEmpty(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;
        var trimmed = key.Trim();
        return trimmed.Equals("YOUR_GROQ_API_KEY", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}
