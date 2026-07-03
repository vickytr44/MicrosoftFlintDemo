using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services.Implementations;

/// <summary>
/// Orchestrates the startup connection sequence and constructs the Flint Chart Agent.
/// </summary>
public sealed class FlintAgentInitializer(
    IMcpService mcpService,
    IChatClient chatClient,
    IPromptProvider promptProvider,
    IChartStateReader stateReader) : IAgentInitializer
{
    public async Task<AIAgent> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("⏳ Starting Flint Chart MCP server...");
        var mcpTools = await mcpService.ConnectAsync(cancellationToken);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Connected! Found {mcpTools.Count} Flint chart tools:");
        Console.ResetColor();
        foreach (var tool in mcpTools)
        {
            var desc = tool.Description ?? "";
            var truncated = desc.Length > 80 ? desc[..80] + "..." : desc;
            Console.WriteLine($"   🔧 {tool.Name}: {truncated}");
        }

        // Create the ChatClientAgent with the MCP tools
        var chatClientAgent = new ChatClientAgent(
            chatClient,
            instructions: promptProvider.SystemPrompt,
            name: "FlintChartAgent",
            description: "A data visualization assistant that creates charts using the Flint chart system.",
            tools: [.. mcpTools]);

        // Wrap with shared-state for CopilotKit co-agent state synchronization
        var agent = new FlintSharedStateAgent(chatClientAgent, stateReader);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n🚀 Flint Chart AGUI Agent initialized successfully.");
        Console.ResetColor();

        return agent;
    }
}
