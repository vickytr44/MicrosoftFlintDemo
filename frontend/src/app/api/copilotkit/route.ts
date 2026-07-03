import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { NextRequest, NextResponse } from "next/server";

// 1. Use the empty adapter since we only have a single C# AGUI agent
const serviceAdapter = new ExperimentalEmptyAdapter();

// 2. Create the CopilotRuntime pointing at our C# AGUI server
const runtime = new CopilotRuntime({
  agents: {
    flint_agent: new HttpAgent({ url: "http://127.0.0.1:5000/agent" }),
  },
});

// 3. CORS headers
const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "*",
};

// 4. Preflight
export async function OPTIONS() {
  return NextResponse.json({}, { headers: corsHeaders });
}

// 5. Shared handler (GET + POST)
async function handle(req: NextRequest) {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter,
    endpoint: "/api/copilotkit",
  });

  const res = await handleRequest(req);

  // Attach CORS headers
  Object.entries(corsHeaders).forEach(([k, v]) => res.headers.set(k, v));

  return res;
}

// 6. GET (for /info)
export const GET = handle;

// 7. POST (chat + streaming)
export const POST = handle;
