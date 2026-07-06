using FlintChartAgent.Services.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services.Implementations;

/// <summary>
/// Orchestrates the startup connection sequence and constructs the Flint Chart Agent.
/// </summary>
public sealed class FlintAgentInitializer(
    IMcpService mcpService,
    IChatClient chatClient,
    ILoggerFactory loggerFactory,
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

        // Build the AgentSkillsProvider that discovers skills over MCP
        AgentSkillsProvider? skillsProvider = null;
        if (mcpService.Client is not null)
        {
            try
            {
                skillsProvider = new AgentSkillsProviderBuilder()
                    .UseMcpSkills(mcpService.Client)
                    .Build();
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("📖 Registered AgentSkillsProvider for Flint Chart MCP skills!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to build MCP Skills Provider: {ex.Message}");
            }
        }

        // Configure the ChatClientAgent using options
        var agentOptions = new ChatClientAgentOptions
        {
            Name = "FlintChartAgent",
            Description = "A data visualization assistant that creates charts using the Flint chart system.",
            ChatOptions = new()
            {
                //Instructions = promptProvider.SystemPrompt,
                Tools = [.. mcpTools]
            }
        };

        if (skillsProvider is not null)
        {
            agentOptions.AIContextProviders = [skillsProvider];
        }

        // Create the ChatClientAgent
        var chatClientAgent = new ChatClientAgent(
            chatClient,
            agentOptions,
            loggerFactory: loggerFactory);

        // Wrap with shared-state for CopilotKit co-agent state synchronization
        var agent = new FlintSharedStateAgent(
            chatClientAgent.AsBuilder().UseLogging(loggerFactory).Build(),
            stateReader,
            mcpTools);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n🚀 Flint Chart AGUI Agent initialized successfully.");
        Console.ResetColor();

        return agent;
    }
}
