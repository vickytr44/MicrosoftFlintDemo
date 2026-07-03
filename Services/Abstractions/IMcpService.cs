using ModelContextProtocol.Client;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Defines the lifecycle management contract for connecting to the Flint Chart MCP server.
/// </summary>
public interface IMcpService : IAsyncDisposable
{
    /// <summary>
    /// Establishes a connection to the MCP server and returns the discovered tools.
    /// </summary>
    Task<IList<McpClientTool>> ConnectAsync(CancellationToken cancellationToken = default);
}
