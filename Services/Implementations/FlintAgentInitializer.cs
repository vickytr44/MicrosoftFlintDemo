using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FlintChartAgent.Services.Abstractions;

namespace FlintChartAgent.Services.Implementations;

/// <summary>
/// Orchestrates the startup connection sequence and constructs the Flint Chart Agent.
/// </summary>
public sealed class FlintAgentInitializer : IAgentInitializer
{
    private readonly IMcpService _mcpService;
    private readonly IChatClient _chatClient;
    private readonly IPromptProvider _promptProvider;
    private readonly IChartStateReader _stateReader;

    public FlintAgentInitializer(
        IMcpService mcpService,
        IChatClient chatClient,
        IPromptProvider promptProvider,
        IChartStateReader stateReader)
    {
        _mcpService = mcpService;
        _chatClient = chatClient;
        _promptProvider = promptProvider;
        _stateReader = stateReader;
    }

    public async Task<AIAgent> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("⏳ Starting Flint Chart MCP server...");
        var mcpTools = await _mcpService.ConnectAsync(cancellationToken);

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
            _chatClient,
            instructions: _promptProvider.SystemPrompt,
            name: "FlintChartAgent",
            description: "A data visualization assistant that creates charts using the Flint chart system.",
            tools: [.. mcpTools]);

        var jsonOptions = JsonSerializerOptions.Default;

        // Wrap with shared-state for CopilotKit co-agent state synchronization
        var agent = new FlintSharedStateAgent(chatClientAgent, _stateReader, jsonOptions);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n🚀 Flint Chart AGUI Agent initialized successfully.");
        Console.ResetColor();

        return agent;
    }
}
