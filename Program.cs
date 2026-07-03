using System.ClientModel;
using System.Text.Json;
using FlintChartAgent.Configuration;
using FlintChartAgent.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

// ──────────────────────────────────────────────
//  1. Build the ASP.NET Core Application
// ──────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// Load appsettings & development overrides
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// ──────────────────────────────────────────────
//  2. Configure Services (DI)
// ──────────────────────────────────────────────

// Bind strongly-typed settings from configuration
builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection(LlmSettings.SectionName));
builder.Services.Configure<McpSettings>(builder.Configuration.GetSection(McpSettings.SectionName));

// Logging
builder.Services.AddLogging(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Warning);
});

// Add CORS for the Next.js frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register AGUI services
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(FlintAgentSerializerContext.Default));
builder.Services.AddAGUI();

// MCP service (Flint Chart server connection)
builder.Services.AddSingleton<FlintMcpService>();

// Chart State Manager (shared singleton)
builder.Services.AddSingleton<ChartStateManager>();

// ApiKeyCredential
builder.Services.AddSingleton<ApiKeyCredential>(sp =>
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
builder.Services.AddSingleton<IChatClient>(sp =>
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

// ──────────────────────────────────────────────
//  3. Build the application and configure the pipeline
// ──────────────────────────────────────────────
var app = builder.Build();
app.UseCors();

// ──────────────────────────────────────────────
//  4. Connect to MCP and create the AI Agent
// ──────────────────────────────────────────────
var mcpService = app.Services.GetRequiredService<FlintMcpService>();
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
var systemPrompt = """
    You are a helpful data visualization assistant powered by the Flint chart system.
    You have access to Flint Chart MCP tools that let you create beautiful charts.

    When a user asks for a chart:
    1. First use 'list_chart_types' to discover available chart types if you need guidance.
    2. Construct the tool arguments for Flint MCP tools (like 'render_chart', 'compile_chart', 'validate_chart', or 'create_chart_view') carefully.
    
    The Flint MCP tools expect arguments matching this schema:
    - 'data': Object containing either '{ "values": [...] }' (for inline data rows) or '{ "url": "..." }' (for local files).
    - 'semantic_types': (Optional) Object mapping field/column names to semantic types (e.g. "Month", "Quantity", "Price", "Country", "Percentage", "Temperature", "Year").
    - 'chart_spec': Object describing the chart design:
        - 'chartType': String specifying the chart template name exactly (e.g., "Bar Chart", "Line Chart", "Pie Chart", "Scatter Plot", "Grouped Bar Chart", "Stacked Bar Chart", "Lollipop Chart", "Area Chart").
        - 'encodings': Object mapping visual channels (like "x", "y", "color", "theta", "size") to their field names:
            e.g., { "x": { "field": "month" }, "y": { "field": "sales" } }
        - 'baseSize': (Optional) Object with 'width' and 'height' (numbers).
        - 'chartProperties': (Optional) Object for fine-tuning.

    Example of correct tool arguments to pass to 'render_chart' or 'create_chart_view':
    {
      "data": {
        "values": [
          {"month": "Jan", "sales": 100},
          {"month": "Feb", "sales": 200},
          {"month": "Mar", "sales": 150}
        ]
      },
      "semantic_types": {
        "month": "Month",
        "sales": "Quantity"
      },
      "chart_spec": {
        "chartType": "Bar Chart",
        "encodings": {
          "x": { "field": "month" },
          "y": { "field": "sales" }
        }
      },
      "backend": "vegalite",
      "format": "png"
    }

    Key Guidelines:
    - Do not pass 'data' or 'semantic_types' inside the 'chart_spec' object; they must be sibling properties at the root level of the tool parameters.
    - Ensure all field references in the 'encodings' match the column names in the 'data' exactly.
    - Use correct channel names for the chart type (e.g., color, x, y, theta for Pie/Donut).
    - Always provide sample data or compile/render the requested chart using the provided tools.
    """;

// Create the ChatClientAgent with the MCP tools
var chatClientAgent = new ChatClientAgent(
    chatClient,
    instructions: systemPrompt,
    name: "FlintChartAgent",
    description: "A data visualization assistant that creates charts using the Flint chart system.",
    tools: [.. mcpTools]);

// Wrap with shared-state for CopilotKit co-agent state synchronization
var agent = new FlintSharedStateAgent(chatClientAgent, jsonOptions);

// Map the AG-UI agent endpoint
app.MapAGUI("/agent", agent);

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("\n🚀 Flint Chart AGUI Server is running at http://localhost:5000/agent");
Console.WriteLine("   Connect CopilotKit frontend to this endpoint.");
Console.ResetColor();

await app.RunAsync();

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

public partial class Program { }
