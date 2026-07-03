/**
 * State of the Flint Chart agent, synchronized via useCoAgent.
 * This must align with the C# FlintStateSnapshot definition.
 */
export type AgentState = {
  charts: ChartData[];
};

export type ChartData = {
  id: string;
  timestamp: string;
  prompt: string;
  flintSpec: string;
  compiledSpec: string;
  backend: string;
};
