using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.ClientModel;

namespace FlintChartAgent.Services;

/// <summary>
/// Orchestrates the interactive chat loop between the user, the LLM, and the Flint MCP tools.
/// </summary>
public sealed class AgentChatService
{
    private readonly IChatClient _chatClient;
    private readonly ApiKeyCredential _credential;
    private readonly ILogger<AgentChatService> _logger;

    private static readonly string SystemPrompt = """
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

    public AgentChatService(IChatClient chatClient, ApiKeyCredential credential, ILogger<AgentChatService> logger)
    {
        _chatClient = chatClient;
        _credential = credential;
        _logger = logger;
    }

    /// <summary>
    /// Runs the interactive chat loop until the user exits.
    /// </summary>
    public async Task RunAsync(IList<McpClientTool> tools, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt)
        };

        var chatOptions = new ChatOptions
        {
            // McpClientTool implements AIFunction (which is an AITool), so they can be used directly.
            Tools = [.. tools]
        };

        PrintBanner(tools);

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\n📊 You: ");
            Console.ResetColor();

            var userInput = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            if (IsExitCommand(userInput))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n👋 Goodbye! Happy charting!");
                Console.ResetColor();
                break;
            }

            messages.Add(new ChatMessage(ChatRole.User, userInput));

            var success = false;
            while (!success)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("   ⏳ Thinking...");
                    Console.ResetColor();

                    _logger.LogDebug("Sending message to LLM with {ToolCount} tools available.", chatOptions.Tools?.Count);

                    var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

                    messages.AddRange(response.Messages);

                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("\n🤖 Agent: ");
                    Console.ResetColor();
                    Console.WriteLine(response.Text ?? "(no text response)");
                    success = true;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (IsApiKeyException(ex))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n🔑 The configured Groq API Key is invalid or unauthorized (401).");
                    Console.Write("Please enter a valid API key: ");
                    Console.ResetColor();
                    var newKey = Console.ReadLine()?.Trim();
                    Console.WriteLine();

                    if (!string.IsNullOrWhiteSpace(newKey))
                    {
                        _credential.Update(newKey);
                        // Retrying next iteration of this inner loop...
                    }
                    else
                    {
                        messages.RemoveAt(messages.Count - 1);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during chat completion.");

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n❌ Error: {ex.Message}");
                    Console.ResetColor();

                    messages.RemoveAt(messages.Count - 1);
                    break;
                }
            }
        }
    }

    private static bool IsApiKeyException(Exception ex)
    {
        var str = ex.ToString();
        return str.Contains("invalid_api_key") || str.Contains("401") || str.Contains("Unauthorized");
    }

    private static bool IsExitCommand(string input)
        => input.Equals("exit", StringComparison.OrdinalIgnoreCase)
        || input.Equals("quit", StringComparison.OrdinalIgnoreCase)
        || input.Equals("q", StringComparison.OrdinalIgnoreCase);

    private static void PrintBanner(IList<McpClientTool> tools)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║   🪄 Flint Chart Agent — Powered by MCP + AI    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Connected! Found {tools.Count} Flint chart tools:");
        Console.ResetColor();

        foreach (var tool in tools)
        {
            var desc = tool.Description ?? "";
            var truncated = desc.Length > 80 ? desc[..80] + "..." : desc;
            Console.WriteLine($"   🔧 {tool.Name}: {truncated}");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("💡 Try asking things like:");
        Console.ResetColor();
        Console.WriteLine("   • \"Create a bar chart of quarterly revenue\"");
        Console.WriteLine("   • \"Show me a line chart of temperature over time\"");
        Console.WriteLine("   • \"What chart types are available?\"");
        Console.WriteLine("   • \"Make a pie chart of market share by company\"");
        Console.WriteLine();
        Console.WriteLine("Type 'exit', 'quit', or 'q' to stop.");
        Console.WriteLine(new string('─', 50));
    }
}
