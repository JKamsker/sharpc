namespace ShaRPC.Core.Server;

/// <summary>
/// Interface for the ShaRPC server.
/// </summary>
public interface IShaRpcServer : IAsyncDisposable
{
    /// <summary>
    /// Starts the server and begins accepting connections.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the server gracefully.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
}
