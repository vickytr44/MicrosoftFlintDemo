using FlintChartAgent.Configuration;
using FlintChartAgent.Services.Abstractions;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure the Application Configuration
builder.ConfigureAppConfiguration();

// 2. Register all Flint Chart Agent services in Dependency Injection
builder.Services.AddFlintAgentServices(builder.Configuration);

var app = builder.Build();

app.UseCors();

// 3. Connect to MCP and initialize the Agent
var initializer = app.Services.GetRequiredService<IAgentInitializer>();
var agent = await initializer.InitializeAsync();

// 4. Map the AG-UI agent endpoint
app.MapAGUI("/agent", agent);

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("\n🚀 Flint Chart AGUI Server is running at http://localhost:5000/agent");
Console.WriteLine("   Connect CopilotKit frontend to this endpoint.");
var llmSettings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
var activeSettings = llmSettings.GetActiveSettings();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"   Using API Provider: {llmSettings.Provider}");
Console.WriteLine($"   Using Model:        {activeSettings.Model}");
Console.ResetColor();

// 5. Run the application
await app.RunAsync();

public partial class Program { }
