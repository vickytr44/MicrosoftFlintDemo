#pragma warning disable MAAI001
#pragma warning disable MEAI001

using FlintChartAgent.Services.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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

        // Pre-load the flint-chart-author skill content from MCP
        string? skillContent = null;
        if (mcpService.Client is not null)
        {
            try
            {
                Console.WriteLine("[FLINT] Pre-loading flint-chart-author skill from MCP resource 'flint://agent-skill'...");
                var resourceResult = await mcpService.Client.ReadResourceAsync("flint://agent-skill");
                if (resourceResult?.Contents != null && resourceResult.Contents.Count > 0)
                {
                    if (resourceResult.Contents[0] is TextResourceContents textContent)
                    {
                        skillContent = textContent.Text;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[FLINT] ✅ Skill content successfully loaded ({skillContent.Length} chars).");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FLINT] ⚠️ Failed to load skill from flint://agent-skill: {ex.Message}");
            }

            if (string.IsNullOrEmpty(skillContent))
            {
                try
                {
                    Console.WriteLine("[FLINT] Falling back to loading skill from MCP prompt 'author_flint_chart'...");
                    var promptResult = await mcpService.Client.GetPromptAsync("author_flint_chart", cancellationToken: cancellationToken);
                    if (promptResult?.Messages != null && promptResult.Messages.Count > 0)
                    {
                        var textParts = new List<string>();
                        foreach (var msg in promptResult.Messages)
                        {
                            if (msg.Content is TextContentBlock textBlock)
                            {
                                textParts.Add(textBlock.Text);
                            }
                        }
                        if (textParts.Count > 0)
                        {
                            skillContent = string.Join("\n", textParts);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[FLINT] ✅ Fallback skill content successfully loaded ({skillContent.Length} chars).");
                            Console.ResetColor();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FLINT] ⚠️ MCP fallback prompt 'author_flint_chart' failed: {ex.Message}");
                }
            }
        }

        // Build the AgentSkillsProvider and register our dynamic inline skill
        AgentSkillsProvider? skillsProvider = null;
        if (!string.IsNullOrEmpty(skillContent))
        {
            try
            {
                var flintSkill = new AgentInlineSkill(
                    name: "flint-chart-author",
                    description: "Provides instructions for authoring Flint charts, including valid chart types and channel encodings.",
                    instructions: skillContent
                );

                skillsProvider = new AgentSkillsProviderBuilder()
                    .UseSkill(flintSkill)
                    .Build();
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("📖 Registered AgentInlineSkill for flint-chart-author!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to build Agent Inline Skill: {ex.Message}");
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
            loggerFactory: loggerFactory)
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseToolApproval(new ToolApprovalAgentOptions
            {
                AutoApprovalRules = [AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule]
            })
            .Build();

        // Wrap with shared-state for CopilotKit co-agent state synchronization
        var agent = new FlintSharedStateAgent(
            chatClientAgent,
            stateReader,
            mcpTools);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n🚀 Flint Chart AGUI Agent initialized successfully.");
        Console.ResetColor();

        return agent;
    }
}
