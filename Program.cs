using FlintChartAgent.Configuration;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure the Application Configuration
builder.ConfigureAppConfiguration();

// 2. Register all Flint Chart Agent services in Dependency Injection
builder.Services.AddFlintAgentServices(builder.Configuration);

var app = builder.Build();

app.UseCors();

// 3. Connect to MCP, initialize agent and map endpoints
await app.InitializeFlintAgentAsync();

// 4. Run the application
await app.RunAsync();

public partial class Program { }
