using Microsoft.Extensions.AI;

namespace FlintChartAgent.Services.Abstractions;

/// <summary>
/// Defines the contract for constructing and configuring the IChatClient instance.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Creates and configures the IChatClient with required middleware/interceptors.
    /// </summary>
    IChatClient CreateChatClient();
}
