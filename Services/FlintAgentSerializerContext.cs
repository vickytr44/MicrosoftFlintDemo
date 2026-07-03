using System.Text.Json.Serialization;

namespace FlintChartAgent.Services;

[JsonSerializable(typeof(FlintStateSnapshot))]
[JsonSerializable(typeof(ChartDataSnapshot))]
internal sealed partial class FlintAgentSerializerContext : JsonSerializerContext;
