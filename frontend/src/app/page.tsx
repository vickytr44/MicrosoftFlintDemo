"use client";

import { ChartCard } from "@/components/ChartCard";
import { AgentState } from "@/lib/types";
import {
  useCoAgent,
  useRenderToolCall,
} from "@copilotkit/react-core";
import { CopilotSidebar, CopilotKitCSSProperties } from "@copilotkit/react-ui";

const CHART_TOOL_NAMES = ["compile_chart", "render_chart", "create_chart_view"];

export default function FlintDashboard() {
  return (
    <main
      style={
        { "--copilot-kit-primary-color": "#7c3aed" } as CopilotKitCSSProperties
      }
    >
      <CopilotSidebar
        disableSystemMessage={true}
        clickOutsideToClose={false}
        labels={{
          title: "Flint Chart Agent",
          initial:
            "👋 Hi! I'm your data visualization assistant. Ask me to create any chart — bar, line, pie, scatter, and more!",
        }}
        suggestions={[
          {
            title: "Bar Chart",
            message: "Create a bar chart of quarterly revenue for Q1-Q4 with sample data.",
          },
          {
            title: "Pie Chart",
            message:
              "Make a pie chart showing market share: Google 45%, Apple 30%, Microsoft 15%, Others 10%.",
          },
          {
            title: "Line Chart",
            message:
              "Show me a line chart of monthly temperature in New York from Jan to Dec with sample data.",
          },
          {
            title: "Chart Types",
            message: "What chart types are available?",
          },
        ]}
      >
        <DashboardContent />
      </CopilotSidebar>
    </main>
  );
}

function DashboardContent() {
  // Shared state with the C# agent
  const { state } = useCoAgent<AgentState>({
    name: "flint_agent",
    initialState: {
      charts: [],
    },
  });

  // Register Generative UI renderers for all chart tools
  for (const toolName of CHART_TOOL_NAMES) {
    // eslint-disable-next-line react-hooks/rules-of-hooks
    useRenderToolCall({
      name: toolName,
      render: ({ args, status, result }: { args: Record<string, unknown>; status: string; result?: unknown }) => {
        // Find matching appHtml from co-agent state if complete
        let appHtml: string | undefined = undefined;
        if (status === "complete" && state?.charts) {
          const matchingChart = state.charts.find(c => {
            try {
              const spec = typeof c.flintSpec === "string" ? JSON.parse(c.flintSpec) : c.flintSpec;
              return JSON.stringify(spec) === JSON.stringify(args);
            } catch {
              return false;
            }
          });
          appHtml = matchingChart?.appHtml;
        }

        return (
          <ChartCard
            args={args}
            status={status as "inProgress" | "executing" | "complete"}
            result={result}
            appHtml={appHtml}
          />
        );
      },
    });
  }

  const charts = state?.charts ?? [];

  return (
    <div className="dashboard">
      {/* Header */}
      <header className="dashboard-header">
        <div style={{ display: "flex", alignItems: "center" }}>
          <h1>📊 Flint Chart Agent</h1>
          <span className="subtitle">AI-Powered Data Visualization</span>
        </div>
        <div className="status-badge">
          <span className="status-dot" />
          AGUI Connected
        </div>
      </header>

      {/* Main Content */}
      <div className="dashboard-content">
        {charts.length === 0 ? (
          <div className="empty-state">
            <div className="empty-state-icon">✨</div>
            <h2>Ready to Visualize</h2>
            <p>
              Open the chat sidebar and describe the chart you want. The AI
              agent will generate interactive visualizations powered by the
              Flint chart engine.
            </p>
            <div className="suggestion-chips">
              <span className="suggestion-chip">Bar Chart</span>
              <span className="suggestion-chip">Line Chart</span>
              <span className="suggestion-chip">Pie Chart</span>
              <span className="suggestion-chip">Scatter Plot</span>
              <span className="suggestion-chip">Heatmap</span>
            </div>
          </div>
        ) : (
          <div className="charts-grid">
            {charts.map((chart) => {
              let compiledSpec = null;
              try {
                compiledSpec =
                  typeof chart.compiledSpec === "string"
                    ? JSON.parse(chart.compiledSpec)
                    : chart.compiledSpec;
              } catch {
                // ignore
              }

              let flintArgs = {};
              try {
                flintArgs =
                  typeof chart.flintSpec === "string"
                    ? JSON.parse(chart.flintSpec)
                    : chart.flintSpec || {};
              } catch {
                // ignore
              }

              return (
                <div key={chart.id} className="chart-card">
                  <div className="chart-card-header">
                    <span className="chart-card-title">{chart.prompt}</span>
                    <span className="chart-card-badge">{chart.backend}</span>
                  </div>
                  <ChartCard
                    args={flintArgs as Record<string, unknown>}
                    status="complete"
                    result={compiledSpec}
                    appHtml={chart.appHtml}
                  />
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
