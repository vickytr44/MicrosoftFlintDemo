import { useState, useEffect, useRef } from 'react';
import vegaEmbed from 'vega-embed';
import * as echarts from 'echarts';
import Chart from 'chart.js/auto';
import './App.css';

interface ChartData {
  id: string;
  timestamp: string;
  prompt: string;
  flintSpec: any;
  compiledSpec: any;
  backend: string;
}

export default function App() {
  const [charts, setCharts] = useState<ChartData[]>([]);
  const [selectedChartId, setSelectedChartId] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'preview' | 'flint' | 'compiled'>('preview');

  const vegaRef = useRef<HTMLDivElement>(null);
  const echartsRef = useRef<HTMLDivElement>(null);
  const chartjsRef = useRef<HTMLCanvasElement>(null);
  const chartjsInstanceRef = useRef<Chart | null>(null);

  // Connect to Server-Sent Events (SSE) for real-time updates
  useEffect(() => {
    const fetchHistory = async () => {
      try {
        const res = await fetch('/api/charts');
        if (res.ok) {
          const history = await res.json();
          setCharts(history);
          if (history.length > 0) {
            setSelectedChartId(history[history.length - 1].id);
          }
        }
      } catch (err) {
        console.error("Failed to load chart history", err);
      }
    };

    fetchHistory();

    const eventSource = new EventSource('/api/chart-sse');
    eventSource.onmessage = (event) => {
      try {
        const newChart: ChartData = JSON.parse(event.data);
        setCharts((prev) => {
          // Prevent duplicates
          if (prev.some((c) => c.id === newChart.id)) return prev;
          const updated = [...prev, newChart];
          setSelectedChartId(newChart.id);
          return updated;
        });
      } catch (err) {
        console.error("Failed to parse SSE message", err);
      }
    };

    return () => {
      eventSource.close();
    };
  }, []);

  const selectedChart = charts.find((c) => c.id === selectedChartId) || null;

  // Check if compiledSpec is actually a raw Flint spec (needs translation to Vega-Lite)
  const isFlintSpec = !!(selectedChart && selectedChart.compiledSpec && selectedChart.compiledSpec.chart_spec);
  const rawBackend = selectedChart?.backend?.toLowerCase() || 'vegalite';
  const renderer = isFlintSpec ? 'vegalite' : rawBackend;

  // Translate Flint spec to Vega-Lite spec on the fly if needed
  const getRenderSpec = (chart: ChartData) => {
    const spec = chart.compiledSpec;
    if (!spec || !spec.chart_spec) {
      return spec;
    }

    const flintChart = spec.chart_spec;
    const chartType = flintChart.chartType?.toLowerCase() || '';
    
    let mark: any = 'bar';
    let encoding: any = {};

    // Map mark types
    if (chartType.includes('pie') || chartType.includes('donut')) {
      mark = { type: 'arc', tooltip: true };
      
      const colorField = flintChart.encodings?.color?.field || flintChart.encodings?.theta?.field;
      const thetaField = flintChart.encodings?.theta?.field || flintChart.encodings?.size?.field || flintChart.encodings?.y?.field;
      
      encoding = {
        theta: { field: thetaField, type: 'quantitative' },
        color: { field: colorField, type: 'nominal' }
      };

      if (chartType.includes('donut')) {
        mark.innerRadius = 50;
      }
    } else if (chartType.includes('line')) {
      mark = { type: 'line', point: true, tooltip: true };
    } else if (chartType.includes('scatter')) {
      mark = { type: 'point', tooltip: true };
    } else if (chartType.includes('area')) {
      mark = { type: 'area', tooltip: true };
    } else {
      mark = { type: 'bar', tooltip: true };
    }

    // Default encodings mapping for x/y charts
    if (!chartType.includes('pie') && !chartType.includes('donut')) {
      const xField = flintChart.encodings?.x?.field;
      const yField = flintChart.encodings?.y?.field;
      const colorField = flintChart.encodings?.color?.field;

      encoding = {
        x: { field: xField, type: 'nominal', axis: { labelAngle: 0 } },
        y: { field: yField, type: 'quantitative' }
      };

      if (colorField) {
        encoding.color = { field: colorField, type: 'nominal' };
      }
    }

    const width = flintChart.baseSize?.width || 500;
    const height = flintChart.baseSize?.height || 400;

    return {
      $schema: 'https://vega.github.io/schema/vega-lite/v5.json',
      description: chart.prompt || 'Generated Chart',
      data: spec.data || { values: [] },
      width: width,
      height: height,
      mark: mark,
      encoding: encoding,
      config: {
        background: 'transparent',
        view: { stroke: 'transparent' }
      }
    };
  };

  // Render the selected chart depending on the compiled backend
  useEffect(() => {
    if (!selectedChart || activeTab !== 'preview') return;

    const spec = getRenderSpec(selectedChart);

    // Cleanup previous chartjs
    if (chartjsInstanceRef.current) {
      chartjsInstanceRef.current.destroy();
      chartjsInstanceRef.current = null;
    }

    if (renderer === 'vegalite' && vegaRef.current) {
      vegaEmbed(vegaRef.current, spec, { 
        actions: true,
        theme: 'dark'
      }).catch((err) => console.error("VegaLite render error", err));
    } 
    else if (renderer === 'echarts' && echartsRef.current) {
      const existingInstance = echarts.getInstanceByDom(echartsRef.current);
      if (existingInstance) {
        existingInstance.dispose();
      }
      const chart = echarts.init(echartsRef.current, 'dark');
      chart.setOption(spec);
    } 
    else if (renderer === 'chartjs' && chartjsRef.current) {
      try {
        const ctx = chartjsRef.current.getContext('2d');
        if (ctx) {
          chartjsInstanceRef.current = new Chart(ctx, spec);
        }
      } catch (err) {
        console.error("Chart.js render error", err);
      }
    }
  }, [selectedChart, activeTab, renderer]);

  return (
    <div className="app-container">
      {/* Sidebar */}
      <aside className="sidebar">
        <div className="brand">
          <span className="logo-spark">🪄</span>
          <h2>Flint Dashboard</h2>
        </div>
        <div className="history-header">
          <h3>History</h3>
          <span className="badge">{charts.length}</span>
        </div>
        <div className="history-list">
          {charts.length === 0 ? (
            <p className="no-charts">No charts generated yet. Ask the agent in the terminal!</p>
          ) : (
            charts.slice().reverse().map((c) => (
              <button
                key={c.id}
                className={`history-item ${c.id === selectedChartId ? 'active' : ''}`}
                onClick={() => setSelectedChartId(c.id)}
              >
                <div className="item-meta">
                  <span className="item-time">{new Date(c.timestamp).toLocaleTimeString()}</span>
                  <span className="item-backend">{c.backend}</span>
                </div>
                <p className="item-prompt">{c.prompt || "Generated Chart"}</p>
              </button>
            ))
          )}
        </div>
      </aside>

      {/* Main Content Area */}
      <main className="main-content">
        {selectedChart ? (
          <div className="chart-card glass">
            {/* Header */}
            <div className="chart-header">
              <div>
                <span className="prompt-label">Prompt</span>
                <h1>{selectedChart.prompt || "Generated Visualization"}</h1>
              </div>
              <div className="chart-actions">
                <span className="badge-backend">{selectedChart.backend}</span>
              </div>
            </div>

            {/* Tab Navigation */}
            <div className="tabs">
              <button 
                className={activeTab === 'preview' ? 'active' : ''} 
                onClick={() => setActiveTab('preview')}
              >
                📊 Preview
              </button>
              <button 
                className={activeTab === 'flint' ? 'active' : ''} 
                onClick={() => setActiveTab('flint')}
              >
                🪨 Flint Spec
              </button>
              <button 
                className={activeTab === 'compiled' ? 'active' : ''} 
                onClick={() => setActiveTab('compiled')}
              >
                ⚡ Compiled Backend Spec
              </button>
            </div>

            {/* Tab Panels */}
            <div className="tab-panel">
              {activeTab === 'preview' && (
                <div className="render-container">
                  {renderer === 'vegalite' && (
                    <div ref={vegaRef} className="vega-view" />
                  )}
                  {renderer === 'echarts' && (
                    <div ref={echartsRef} className="echarts-view" />
                  )}
                  {renderer === 'chartjs' && (
                    <div className="chartjs-wrapper">
                      <canvas ref={chartjsRef} />
                    </div>
                  )}
                </div>
              )}

              {activeTab === 'flint' && (
                <pre className="code-view">
                  <code>{JSON.stringify(selectedChart.flintSpec, null, 2)}</code>
                </pre>
              )}

              {activeTab === 'compiled' && (
                <pre className="code-view">
                  <code>{JSON.stringify(selectedChart.compiledSpec, null, 2)}</code>
                </pre>
              )}
            </div>
          </div>
        ) : (
          <div className="empty-state glass">
            <span className="empty-icon">🪄</span>
            <h2>Welcome to Flint Chart Viewer</h2>
            <p>Generate a chart in the console using natural language, and it will render here instantly!</p>
          </div>
        )}
      </main>
    </div>
  );
}
