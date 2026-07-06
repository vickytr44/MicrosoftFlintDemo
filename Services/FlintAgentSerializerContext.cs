using System.Text.Json.Serialization;

namespace FlintChartAgent.Services;

[JsonSerializable(typeof(FlintStateSnapshot))]
[JsonSerializable(typeof(ChartDataSnapshot))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal sealed partial class FlintAgentSerializerContext : JsonSerializerContext;
