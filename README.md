# 🪄 Flint Chart Agent

A .NET 8 console application that uses **Groq AI** with the **Flint Chart MCP** server to create beautiful data visualizations through natural language.

## Architecture

```
┌──────────────────────┐     stdio      ┌──────────────────────┐
│  .NET Console App    │◄──────────────►│  flint-chart-mcp     │
│                      │   (JSON-RPC)   │  (MCP Server via npx)│
│  ┌────────────────┐  │               └──────────────────────┘
│  │ AgentChatService│  │
│  │ (chat loop)     │  │
│  └───────┬────────┘  │
│          │            │
│  ┌───────▼────────┐  │
│  │ FlintMcpService │  │
│  │ (MCP client)    │  │
│  └────────────────┘  │
└──────────┬───────────┘
           │ HTTPS
           ▼
┌──────────────────────┐
│  Groq API            │
│  (OpenAI-compatible) │
└──────────────────────┘
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/) (for `npx` to run the Flint Chart MCP server)
- A [Groq API key](https://console.groq.com/keys)

## Setup

1. **Navigate to the project directory:**
   ```bash
   cd microsoft-flint
   ```

2. **Set your Groq API key:**

   **Windows (PowerShell):**
   ```powershell
   $env:Llm__ApiKey = "gsk_your-key-here"
   ```

   **Linux/macOS:**
   ```bash
   export Llm__ApiKey="gsk_your-key-here"
   ```

   Or edit `appsettings.json` directly and set the `Llm.ApiKey` field.

3. **Restore and run:**
   ```bash
   dotnet restore
   dotnet run
   ```

## Configuration

All settings live in [`appsettings.json`](appsettings.json) and can be overridden with environment variables using the `__` (double-underscore) convention.

### LLM Settings (`Llm` section)

| Setting    | Env Variable   | Default                          | Description                          |
|------------|----------------|----------------------------------|--------------------------------------|
| `ApiKey`   | `Llm__ApiKey`  | *(required)*                     | API key for the LLM provider         |
| `Model`    | `Llm__Model`   | `llama-3.3-70b-versatile`       | Model identifier                     |
| `Endpoint` | `Llm__Endpoint`| `https://api.groq.com/openai/v1`| OpenAI-compatible API base URL       |

### MCP Settings (`Mcp` section)

| Setting      | Default              | Description                          |
|--------------|----------------------|--------------------------------------|
| `Command`    | `npx`               | Command to launch the MCP server     |
| `Arguments`  | `["-y", "flint-chart-mcp"]` | Arguments for the server command |
| `ServerName` | `flint-chart`        | Display name for the MCP connection  |

### Switching LLM Providers

Since the app uses the OpenAI-compatible API format, you can point it at any compatible provider:

```jsonc
// OpenAI
{
  "Llm": {
    "ApiKey": "sk-...",
    "Model": "gpt-4o",
    "Endpoint": "https://api.openai.com/v1"
  }
}

// Azure OpenAI
{
  "Llm": {
    "ApiKey": "your-azure-key",
    "Model": "gpt-4o",
    "Endpoint": "https://your-resource.openai.azure.com/openai/deployments/gpt-4o"
  }
}
```

## Project Structure

```
microsoft-flint/
├── Configuration/
│   ├── LlmSettings.cs        # Strongly-typed LLM config
│   └── McpSettings.cs        # Strongly-typed MCP config
├── Services/
│   ├── FlintMcpService.cs     # MCP client lifecycle management
│   └── AgentChatService.cs    # Interactive chat orchestration
├── Program.cs                 # Composition root (DI wiring)
├── appsettings.json           # Default configuration
├── FlintChartAgent.csproj     # Project file
└── README.md
```

## Usage

Once running, type natural language requests to create charts:

```
📊 You: Create a bar chart showing sales by quarter

🤖 Agent: I'll create that for you! Let me compile a Flint chart spec...
   [calls compile_chart tool]
   Here's your bar chart specification compiled to Vega-Lite: ...
```

### Example Prompts

- "Create a bar chart of quarterly revenue"
- "Show me a line chart of temperature over 12 months"
- "What chart types are available?"
- "Make a pie chart of market share by company"
- "Create a heatmap of activity by day and hour"

## License

MIT
