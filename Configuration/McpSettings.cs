namespace FlintChartAgent.Configuration;

/// <summary>
/// Strongly-typed settings for the Flint Chart MCP server connection.
/// </summary>
public sealed class McpSettings
{
    public const string SectionName = "Mcp";

    /// <summary>Command to launch the MCP server (e.g., "npx").</summary>
    public string Command { get; set; } = "npx";

    /// <summary>Arguments passed to the MCP server command.</summary>
    public string[] Arguments { get; set; } = ["-y", "flint-chart-mcp"];

    /// <summary>Display name for the MCP server connection.</summary>
    public string ServerName { get; set; } = "flint-chart";
}
