"use client";

import { useEffect, useRef } from "react";

interface ChartCardProps {
  /** The raw tool arguments (containing data, chart_spec, etc.) */
  args: Record<string, unknown>;
  /** The tool result from the agent (compiled spec) */
  result?: unknown;
  /** The current status of the tool call */
  status: "inProgress" | "executing" | "complete";
}

/**
 * Renders a Vega-Lite chart from Flint tool call arguments.
 * Used as Generative UI inside the CopilotKit chat sidebar.
 */
export function ChartCard({ args, result, status }: ChartCardProps) {
  const chartRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (status !== "complete" || !chartRef.current) return;

    // Try to parse the compiled spec from the result
    let vegaSpec: Record<string, unknown> | null = null;

    if (result && typeof result === "string") {
      try {
        vegaSpec = JSON.parse(result);
      } catch {
        // result is not JSON parseable
      }
    } else if (result && typeof result === "object") {
      vegaSpec = result as Record<string, unknown>;
    }

    // If the result contains a nested compiledSpec or spec
    if (vegaSpec && "compiledSpec" in vegaSpec) {
      vegaSpec = vegaSpec.compiledSpec as Record<string, unknown>;
    } else if (vegaSpec && "spec" in vegaSpec) {
      vegaSpec = vegaSpec.spec as Record<string, unknown>;
    }

    // If we still don't have a valid vega spec, try to build one from the args
    if (!vegaSpec || !isVegaLikeSpec(vegaSpec)) {
      vegaSpec = translateFlintToVegaLite(args);
    }

    if (!vegaSpec) return;
    const spec = vegaSpec;

    // Auto-enrich the Vega-Lite spec to ensure interactivity (tooltips & zoom/pan scales)
    // 1. Enable standard tooltips on the mark
    if (spec.mark) {
      if (typeof spec.mark === "string") {
        spec.mark = { type: spec.mark, tooltip: true };
      } else if (typeof spec.mark === "object" && spec.mark !== null) {
        (spec.mark as any).tooltip = true;
      }
    }

    const isArc = spec.mark === "arc" || (typeof spec.mark === "object" && spec.mark !== null && (spec.mark as any).type === "arc");

    // 2. Make non-arc charts responsive to parent container width
    if (!isArc) {
      spec.width = "container";
    }
    // Dynamically import vega-embed to avoid SSR issues
    import("vega-embed").then(({ default: vegaEmbed }) => {
      if (!chartRef.current) return;

      vegaEmbed(chartRef.current, spec as any, {
        theme: "dark",
        actions: false,
        renderer: "svg",
        config: {
          background: "transparent",
          view: { stroke: "transparent" },
          axis: {
            labelColor: "#8b8b9e",
            titleColor: "#f0f0f5",
            gridColor: "rgba(255,255,255,0.06)",
            domainColor: "rgba(255,255,255,0.1)",
          },
          legend: {
            labelColor: "#8b8b9e",
            titleColor: "#f0f0f5",
          },
          title: {
            color: "#f0f0f5",
          },
        },
      }).catch((err: Error) => {
        console.warn("Vega-Embed render error:", err);
        if (chartRef.current) {
          chartRef.current.innerHTML = `<p style="color:#8b8b9e;font-size:0.8rem;padding:12px;">Chart preview unavailable</p>`;
        }
      });
    });
  }, [status, result, args]);

  if (status === "inProgress" || status === "executing") {
    const chartType = getChartType(args);
    return (
      <div className="tool-render-card">
        <div className="tool-render-loading">
          <div className="spinner" />
          <span>Generating {chartType}…</span>
        </div>
      </div>
    );
  }

  if (status === "complete") {
    const chartType = getChartType(args);
    return (
      <div className="chart-card" style={{ margin: "4px 0" }}>
        <div className="chart-card-header">
          <span className="chart-card-title">{chartType}</span>
          <span className="chart-card-badge">vega-lite</span>
        </div>
        <div className="chart-container" ref={chartRef} />
      </div>
    );
  }

  return null;
}

// ─────────────── Helpers ───────────────

function getChartType(args: Record<string, unknown>): string {
  const spec = args.chart_spec as Record<string, unknown> | undefined;
  if (spec && typeof spec.chartType === "string") {
    return spec.chartType;
  }
  const nestedSpec = args.spec as Record<string, unknown> | undefined;
  if (nestedSpec) {
    const innerSpec = nestedSpec.chart_spec as Record<string, unknown> | undefined;
    if (innerSpec && typeof innerSpec.chartType === "string") {
      return innerSpec.chartType;
    }
  }
  return "Chart";
}

function isVegaLikeSpec(obj: Record<string, unknown>): boolean {
  return "$schema" in obj || ("mark" in obj && "encoding" in obj) || "layer" in obj;
}

/**
 * Translates a Flint-format spec (data + chart_spec + semantic_types) into a
 * basic Vega-Lite specification for rendering with tooltip interactions.
 */
function translateFlintToVegaLite(
  args: Record<string, unknown>
): Record<string, unknown> | null {
  const data = args.data as Record<string, unknown> | undefined;
  const chartSpec = args.chart_spec as Record<string, unknown> | undefined;

  if (!data || !chartSpec) return null;

  const chartType = (chartSpec.chartType as string) || "Bar Chart";
  const encodings = (chartSpec.encodings as Record<string, { field: string }>) || {};
  const baseSize = chartSpec.baseSize as { width?: number; height?: number } | undefined;

  const markMap: Record<string, string> = {
    "Bar Chart": "bar",
    "Grouped Bar Chart": "bar",
    "Stacked Bar Chart": "bar",
    "Line Chart": "line",
    "Area Chart": "area",
    "Scatter Plot": "point",
    "Pie Chart": "arc",
    "Donut Chart": "arc",
    "Lollipop Chart": "circle",
    "Heatmap": "rect",
  };

  const mark = markMap[chartType] || "bar";

  // Build Vega-Lite encoding
  const vlEncoding: Record<string, unknown> = {};

  if (mark === "arc") {
    // Pie/Donut chart
    if (encodings.color) {
      vlEncoding.color = { field: encodings.color.field, type: "nominal" };
    }
    if (encodings.size) {
      vlEncoding.theta = { field: encodings.size.field, type: "quantitative" };
    } else if (encodings.theta) {
      vlEncoding.theta = { field: encodings.theta.field, type: "quantitative" };
    }
    vlEncoding.tooltip = [
      encodings.color ? { field: encodings.color.field, type: "nominal" } : null,
      encodings.size ? { field: encodings.size.field, type: "quantitative" } : null,
      encodings.theta ? { field: encodings.theta.field, type: "quantitative" } : null,
    ].filter(Boolean);
  } else {
    if (encodings.x) {
      vlEncoding.x = { field: encodings.x.field, type: "nominal" };
    }
    if (encodings.y) {
      vlEncoding.y = { field: encodings.y.field, type: "quantitative" };
    }
    if (encodings.color) {
      vlEncoding.color = { field: encodings.color.field, type: "nominal" };
    }
    vlEncoding.tooltip = [
      encodings.x ? { field: encodings.x.field, type: "nominal" } : null,
      encodings.y ? { field: encodings.y.field, type: "quantitative" } : null,
      encodings.color ? { field: encodings.color.field, type: "nominal" } : null,
    ].filter(Boolean);
  }

  return {
    $schema: "https://vega.github.io/schema/vega-lite/v5.json",
    data: data,
    mark: mark,
    encoding: vlEncoding,
    width: baseSize?.width || 400,
    height: baseSize?.height || 300,
    background: "transparent",
  };
}
