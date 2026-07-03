# Flint Chart Agent — AGUI + CopilotKit

An AI-powered data visualization assistant that creates beautiful, interactive charts through natural language. Built with the [Microsoft Agent Framework](https://github.com/microsoft/agents) (AGUI protocol) and [CopilotKit](https://docs.copilotkit.ai).

## Architecture

```
┌──────────────────────────────────────────┐
│  Next.js Frontend (port 3000)            │
│  ├─ CopilotSidebar (chat UI)            │
│  ├─ useCoAgent (shared state)           │
│  ├─ useRenderToolCall (Generative UI)   │
│  └─ /api/copilotkit (runtime route)     │
│        │                                 │
│        ▼ HTTP (AG-UI protocol)           │
│  ┌──────────────────────────────┐        │
│  │  C# AGUI Server (port 5000) │        │
│  │  ├─ ChatClientAgent         │        │
│  │  ├─ FlintSharedStateAgent   │        │
│  │  ├─ ChartInterceptor        │        │
│  │  └─ Groq LLM Client        │        │
│  │        │                     │        │
│  │        ▼ Stdio (MCP)         │        │
│  │  ┌─────────────────────┐    │        │
│  │  │  Flint Chart MCP    │    │        │
│  │  │  Server (npx)       │    │        │
│  │  └─────────────────────┘    │        │
│  └──────────────────────────────┘        │
└──────────────────────────────────────────┘
```

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- [Node.js 18+](https://nodejs.org/) with npm
- A Groq API key (or any OpenAI-compatible endpoint)

## Quick Start

### 1. Configure API Key

Copy and edit the development settings:

```bash
cp appsettings.json appsettings.development.json
```

Edit `appsettings.development.json` and set your API key:
```json
{
  "Llm": {
    "ApiKey": "your-groq-api-key-here"
  }
}
```

### 2. Start the C# AGUI Server

```bash
dotnet run
```

The AGUI agent server starts at `http://localhost:5000/agent`.

### 3. Start the Next.js Frontend

```bash
cd frontend
npm install
npm run dev
```

The dashboard opens at `http://localhost:3000`.

### 4. Create Charts!

Open the CopilotKit sidebar and ask the agent to create charts:

- "Create a bar chart of quarterly revenue"
- "Make a pie chart showing market share: Google 45%, Apple 30%, Microsoft 15%"
- "Show me a line chart of monthly temperature"
- "What chart types are available?"

## Project Structure

```
├── Program.cs                         # AGUI server entry point
├── FlintChartAgent.csproj             # C# project (net9.0)
├── appsettings.json                   # Default configuration
├── appsettings.development.json       # Local API keys (gitignored)
├── Configuration/
│   ├── LlmSettings.cs                # LLM endpoint settings
│   └── McpSettings.cs                # MCP server settings
├── Services/
│   ├── FlintSharedStateAgent.cs       # AGUI shared state agent
│   ├── FlintAgentSerializerContext.cs # JSON serialization context
│   ├── ChartInterceptingChatClient.cs # Chart tool interceptor
│   ├── ChartStateManager.cs           # In-memory chart store
│   ├── FlintMcpService.cs             # MCP server connection
│   └── AgentChatService.cs            # (Legacy) console chat service
├── frontend/
│   ├── src/
│   │   ├── app/
│   │   │   ├── api/copilotkit/route.ts  # CopilotKit runtime endpoint
│   │   │   ├── layout.tsx               # Root layout with CopilotKit provider
│   │   │   ├── page.tsx                 # Dashboard with sidebar + Generative UI
│   │   │   └── globals.css              # Premium dark theme
│   │   ├── components/
│   │   │   └── ChartCard.tsx            # Vega-Lite chart renderer
│   │   └── lib/
│   │       └── types.ts                 # Shared state types
│   └── package.json
└── .gitignore
```

## Key Technologies

| Component | Technology |
|-----------|-----------|
| AI Agent Server | ASP.NET Core + Microsoft.Agents.AI (AGUI) |
| LLM Provider | Groq (OpenAI-compatible) |
| Chart Engine | Flint Chart MCP Server |
| Frontend Framework | Next.js 16 + React 19 |
| Chat UI | CopilotKit (Sidebar + Generative UI) |
| Chart Rendering | Vega-Lite + Vega-Embed |
| Protocol | AG-UI (Agent-User Interaction) |

## License

MIT
