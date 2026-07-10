---
name: flint-chart-author
description: "Use when: the user asks to make or render charts with flint-chart, visualize tabular data, generate a ChartAssemblyInput, validate/render through MCP, or add Flint to a JS/TS project. Author the semantic spec, transform data before Flint when needed, install/import Flint only when executable code is needed, and reserve backend-specific style tweaks for after compiling from Flint."
---

# flint-chart: authoring and using a chart spec

## What you produce (and what you do NOT)

Your output is the **spec**: the `chart_spec` and `semantic_types` of a
`ChartAssemblyInput`. You reference data columns **by name**. The host
passes the resulting input to `assembleVegaLite`, `assembleECharts`,
or `assembleChartjs` to get a backend spec.

**You write the input spec, not the output spec.** And critically:

- **DO** emit `chart_spec` (chart type, channel→field mapping, properties)
  and `semantic_types` (field → semantic type).
- **Reference columns by name.** How `data` itself gets bound depends on
  the situation — a URL, a host-side variable, or embedded rows (see "How
  data gets bound"). Embedding is fine for small tables; just don't
  re-serialize a *large* dataset by hand, since that risks truncation and
  silent value corruption and wastes tokens.
- **Transform data before Flint.** If the requested chart needs aggregation,
  filtering, joins, pivots, derived columns, or long/wide reshaping beyond
  Flint's built-in static-series fold, use a coding, notebook, SQL, or data tool
  first. Then author the Flint spec against the transformed table.
- **Style after Flint, only when needed.** Author structure in Flint. For a
  presentation tweak Flint does not express (a reference line, annotation, or
  shaded band), use the Vega-Lite escape hatch — see "Post-Flint style
  customization". Never feed edited Vega-Lite JSON back to `render_chart`.

## When the user wants more than a spec

First decide which workflow the user is asking for:

- **Spec authoring only:** return a `ChartAssemblyInput` or its
  `semantic_types` + `chart_spec` pieces. Do not install packages or write
  renderer code unless asked.
- **MCP chart output:** if Flint MCP tools are available, **default to
  `create_chart_view`** whenever the user asks to see a chart — it opens an
  interactive, live-rendered view with a customization panel, and it validates
  the spec for you. Only fall back to `render_chart` (PNG/SVG) when the host has
  no App UI support or the user explicitly wants a static image. Use
  `validate_chart` to check a spec without rendering, `compile_chart` when the
  user wants backend-native JSON, and `list_chart_types` when you need the
  supported chart catalog.
- **Project integration, only when the user asks for code:** add Flint to an
  app, notebook, script, or agentic product, install/import the library, and
  call an assembler in code. Keep the same `ChartAssemblyInput` contract, then
  let the host render the backend result.

For MCP clients, the server can run with `npx`:

```bash
npx -y flint-chart-mcp
```

For JavaScript or TypeScript projects, install Flint first and add only the
renderer peer dependencies needed by the backend you will render:

```bash
npm install flint-chart
npm install vega vega-lite vega-embed  # browser Vega-Lite rendering
npm install echarts                    # ECharts rendering
npm install chart.js                   # Chart.js rendering
```

Then compile with the requested backend:

```ts
import { assembleChartjs, assembleECharts, assembleVegaLite } from 'flint-chart';

const vegaLiteSpec = assembleVegaLite(input);
const echartsOption = assembleECharts(input);
const chartjsConfig = assembleChartjs(input);
```

Python support is planned for a later release. Until the PyPI package is
published, use the npm package or MCP server for released workflows.

```ts
interface ChartAssemblyInput {
  // Bound by the HOST or by you, depending on the situation (see below).
  data: { values: any[] } | { url: string };
  semantic_types?: Record<string, string>;   // field → semantic type  ← you write this
  chart_spec: {                               //                        ← you write this
    chartType: string;                        // e.g. "Scatter Plot"
    encodings: Record<string, EncodingValue>; // channel → { field, ... } (or array)
    baseSize?: { width: number; height: number };    // target layout size, default 400×320
    canvasSize?: { width: number; height: number };  // optional hard ceiling on stretch
    chartProperties?: Record<string, any>;    // per-chart tuning (optional)
  };
  options?: Record<string, any>;              // global layout options (rarely needed)
}
```

## How data gets bound

Use the binding mode that matches the runtime. Do not mix them.

1. **Direct MCP rendering: embed rows.** When calling `render_chart`,
  `compile_chart`, or `validate_chart`, the tool arguments are JSON. If the
  data is small or already transformed by another tool, pass it as
  `data: { values: [...] }`. Do not pass runtime variable names in
  MCP tool calls — the MCP server cannot see your local variables.
2. **Direct MCP rendering: reference a local file.**
  The `flint-chart-mcp` server can load `data: { url: "..." }` from local
  `.json`, `.csv`, or `.tsv` files. By default any local file the agent can
  name is readable (relative paths resolve against the working directory); a
  hardened deployment may reject local file references entirely via
  `--disable-file-reference` (or `FLINT_MCP_DISABLE_FILE_REFERENCE`), in which
  case pass rows inline with `data.values`. Remote URL
  fetching is disabled. If the data must be transformed first, use a
  coding/data tool to write a small prepared file, then reference that file.
3. **Generated application or notebook code: bind runtime variables.** If the
  user asks you to add Flint to code, write normal data-loading code first and
  pass a real runtime value, e.g. `data: { values: rows }`, to
  `assembleVegaLite`, `assembleECharts`, or `assembleChartjs`. This variable
  pattern is for generated code, not for MCP tool calls.

For spec-only answers, return the `semantic_types` and `chart_spec` pieces and
state how the host should bind data. In the worked examples below, `data` is
shown as `{ values: [] }` to signal "host binds this" — focus on `chart_spec`
and `semantic_types`.

## Data transformation before charting

Flint is a chart compiler, not a data-wrangling layer. If the chart needs grouped
totals, time buckets, filters, joins, pivots, derived ratios, or a long-form
table, transform the data first with a host tool, then bind the prepared table
(see "How data gets bound"). Pick semantic types and channels for the transformed
columns, not for columns that no longer exist.

**Sanity-read the values first — don't chart blind.** Inspect the actual data
with your data tool (distinct values per category column, min/max per measure),
not just the column names, and watch for:

- **Embedded totals.** A category column may mix an aggregate level with its
  parts (e.g. `all` alongside `cage-free`/`caged`, or a `Total` region). Charting
  the total with its parts double-counts and flattens the parts — keep one or the
  other on a stacked/grouped/colored channel, not both.
- **Units.** Check whether a rate is a fraction (0–1) or already a percent
  (0–100) before tagging it `Percentage`; don't scale twice.
- **One real entity.** If your breakdown column has a single distinct value, the
  per-group chart collapses to one mark — the intended breakdown is likely a
  different column.

## Post-Flint style customization

Stay at the Flint level for structure (data, chart type, channels, transforms,
sizing, properties) — Flint specs stay portable and regenerate safely. Drop to
backend JSON only after a valid Flint chart exists, and only for a narrow
presentation change Flint does not express (exact axis/legend/mark styling,
titles, annotations, reference lines, layout polish). Never use it to change the
data, chart type, field mappings, or transforms — fix those upstream.

For a Vega-Lite-specific style tweak:

1. Author and validate the Flint `ChartAssemblyInput`.
2. Render or inspect the Flint chart first, when possible.
3. Call `compile_chart` with `backend: "vegalite"`.
4. Make the smallest necessary style/presentation edit to the returned
  Vega-Lite spec.
5. Render the edited spec in the host environment with a Vega-Lite renderer.

This edited Vega-Lite spec is no longer a portable Flint spec. Do not send it to
`render_chart`; use `render_chart` only for Flint `ChartAssemblyInput`.

## Step 1 — pick `chartType`

Use one of the registered names **exactly**. Vega-Lite is the default and
broadest backend; the table below lists each Vega-Lite chart type, the
channels it accepts, and its tuning properties (see "Chart-level
properties"). Required channels are noted.

| chartType | Channels | Notes / required |
|---|---|---|
| `"Scatter Plot"` | x, y, color, size, opacity, column, row | x + y required |
| `"Regression"` | x, y, size, color, column, row | scatter + fit line; props `regressionMethod`, `polyOrder` |
| `"Connected Scatter Plot"` | x, y, order, color, detail, column, row | x + y required; `order` = connection sequence (time/index), so the line traces a trajectory and may self-cross |
| `"Ranged Dot Plot"` | x, y, color | dumbbell of two x per category |
| `"Strip Plot"` | x, y, color, size, column, row | jittered points; props `stepWidth`, `pointSize`, `opacity` |
| `"Bar Chart"` | x, y, color, opacity, column, row | one discrete + one measure; prop `cornerRadius` |
| `"Grouped Bar Chart"` | x, y, group, column, row | `group` = the clustering category |
| `"Stacked Bar Chart"` | x, y, color, column, row | prop `stackMode` |
| `"Pyramid Chart"` | x, y, color | diverging horizontal bars |
| `"Lollipop Chart"` | x, y, color, column, row | prop `dotSize` |
| `"Waterfall Chart"` | x, y, color, column, row | prop `cornerRadius` |
| `"Gantt Chart"` | y, x, x2, color, detail, column, row | x = start, x2 = end |
| `"Bullet Chart"` | y, x, goal, color, column, row | `goal` required (target) |
| `"Histogram"` | x, color, column, row | x = measure t
