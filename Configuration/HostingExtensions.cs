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
using OpenTelemetry.Trace;

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
            b.AddFilter("Microsoft.Extensions.AI", LogLevel.Trace);
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

        // Configure OpenTelemetry Tracing
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("FlintChartAgent")
                .AddConsoleExporter());

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
            var activeSettings = llmSettings.GetActiveSettings();
            var apiKey = activeSettings.ApiKey;

            if (IsPlaceholderOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    $"LLM API key not configured for provider '{llmSettings.Provider}'. Set 'Llm:{llmSettings.Provider}:ApiKey' in appsettings.development.json " +
                    $"or the corresponding environment variable.");
            }

            return new ApiKeyCredential(apiKey);
        });

        // Chat Client Factory
        services.AddSingleton<IChatClientFactory, FlintChatClientFactory>();

        // Chat client — configured via Chat Client Factory
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<IChatClientFactory>().CreateChatClient());

        // Agent Initializer
        services.AddSingleton<IAgentInitializer, FlintAgentInitializer>();

        return services;
    }

    private static bool IsPlaceholderOrEmpty(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return true;
        var trimmed = key.Trim();
        return trimmed.Equals("YOUR_GROQ_API_KEY", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("YOUR_GROQ_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("YOUR_GEMINI_API_KEY_HERE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}
