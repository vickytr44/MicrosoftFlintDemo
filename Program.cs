using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using FlintChartAgent.Configuration;
using FlintChartAgent.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

// ──────────────────────────────────────────────
//  1. Build Configuration
// ──────────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// ──────────────────────────────────────────────
//  2. Configure Services (DI)
// ──────────────────────────────────────────────
var services = new ServiceCollection();

// Bind strongly-typed settings from configuration
services.Configure<LlmSettings>(configuration.GetSection(LlmSettings.SectionName));
services.Configure<McpSettings>(configuration.GetSection(McpSettings.SectionName));

// Logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
});

// MCP service (Flint Chart server connection)
services.AddSingleton<FlintMcpService>();

// Chart State Manager (shared singleton)
services.AddSingleton<ChartStateManager>();

// ApiKeyCredential — registered as a singleton so it can be updated in-place during runtime if a 401 is encountered
services.AddSingleton<ApiKeyCredential>(sp =>
{
    var llmSettings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
    var apiKey = llmSettings.ApiKey;
    if (IsPlaceholderOrEmpty(apiKey))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("🔑 Groq API Key not found. Please enter your API key: ");
        Console.ResetColor();
        apiKey = Console.ReadLine()?.Trim() ?? string.Empty;
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "LLM API key not configured. Set 'Llm:ApiKey' in appsettings.json, " +
                "the 'Llm__ApiKey' environment variable, or enter it when prompted.");
        }
    }

    return new ApiKeyCredential(apiKey);
});

// Chat client — configured for Groq with custom interceptor middleware
services.AddSingleton<IChatClient>(sp =>
{
    var llmSettings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
    var credential = sp.GetRequiredService<ApiKeyCredential>();
    var stateManager = sp.GetRequiredService<ChartStateManager>();
    
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
        .Use(inner => new ChartInterceptingChatClient(inner, stateManager))
        .Build();
});

// Chat service
services.AddSingleton<AgentChatService>();

// ──────────────────────────────────────────────
//  3. Run the Application & Background Web Host
// ──────────────────────────────────────────────
await using var serviceProvider = services.BuildServiceProvider();

var mcpService = serviceProvider.GetRequiredService<FlintMcpService>();
var chatService = serviceProvider.GetRequiredService<AgentChatService>();
var stateManager = serviceProvider.GetRequiredService<ChartStateManager>();

// Setup background ASP.NET Core server
var webBuilder = WebApplication.CreateBuilder(args);
webBuilder.Logging.ClearProviders(); // Keep terminal clean
webBuilder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000);
});

var webApp = webBuilder.Build();
webApp.UseDefaultFiles();
webApp.UseStaticFiles();

// Serve list of compiled charts
webApp.MapGet("/api/charts", () => Results.Json(stateManager.GetCharts()));

// SSE endpoint to push new charts instantly
webApp.MapGet("/api/chart-sse", async (HttpContext httpContext, CancellationToken ct) =>
{
    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.Append("Cache-Control", "no-cache");
    httpContext.Response.Headers.Append("Connection", "keep-alive");

    await httpContext.Response.WriteAsync("retry: 10000\n\n", ct);
    await httpContext.Response.Body.FlushAsync(ct);

    Action<ChartData> onChartAdded = async (chart) =>
    {
        try
        {
            var json = JsonSerializer.Serialize(chart);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }
        catch
        {
            // Client closed connection
        }
    };

    stateManager.OnChartAdded += onChartAdded;

    try
    {
        await Task.Delay(Timeout.Infinite, ct);
    }
    catch (TaskCanceledException)
    {
        // Disconnected
    }
    finally
    {
        stateManager.OnChartAdded -= onChartAdded;
    }
});

// Start the background web app
var webHostTask = webApp.RunAsync();

try
{
    await using (mcpService)
    {
        Console.WriteLine("⏳ Starting Flint Chart MCP server...");
        var tools = await mcpService.ConnectAsync();

        // Launch Browser
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("🌐 Launching chart dashboard UI at http://localhost:5000...");
        Console.ResetColor();
        OpenBrowser("http://localhost:5000");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await chatService.RunAsync(tools, cts.Token);
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n💥 Fatal error: {ex.Message}");
    Console.ResetColor();
    Environment.ExitCode = 1;
}

// ──────────────────────────────────────────────
//  Helper Functions
// ──────────────────────────────────────────────
static bool IsPlaceholderOrEmpty(string? key)
{
    if (string.IsNullOrWhiteSpace(key)) return true;
    var trimmed = key.Trim();
    return trimmed.Equals("YOUR_GROQ_API_KEY", StringComparison.OrdinalIgnoreCase)
        || trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch
    {
        // Try opening with explorer on Windows as fallback
        try
        {
            Process.Start("explorer.exe", url);
        }
        catch
        {
            // Fail silently
        }
    }
}
