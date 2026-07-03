using FlintChartAgent.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace FlintChartAgent.Services;

/// <summary>
/// Manages the lifecycle of the Flint Chart MCP server connection.
/// Implements <see cref="IAsyncDisposable"/> to cleanly shut down the MCP server process.
/// </summary>
public sealed class FlintMcpService : IAsyncDisposable
{
    private readonly McpSettings _settings;
    private readonly ILogger<FlintMcpService> _logger;
    private McpClient? _client;

    public FlintMcpService(IOptions<McpSettings> settings, ILogger<FlintMcpService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Establishes a connection to the Flint Chart MCP server and returns the discovered tools.
    /// </summary>
    public async Task<IList<McpClientTool>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to MCP server '{ServerName}' via {Command}...",
            _settings.ServerName, _settings.Command);

        var transport = new StdioClientTransport(new()
        {
            Name = _settings.ServerName,
            Command = _settings.Command,
            Arguments = _settings.Arguments
        });

        _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);

        _logger.LogInformation("Connected to MCP server. Discovered {ToolCount} tools.", tools.Count);

        return tools;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            _logger.LogInformation("Shutting down MCP server connection...");
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
