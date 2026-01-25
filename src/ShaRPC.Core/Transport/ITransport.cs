namespace ShaRPC.Core.Transport;

/// <summary>
/// Represents a transport layer for establishing connections.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Establishes a connection (client-side).
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the active connection (client-side).
    /// </summary>
    IConnection? Connection { get; }

    /// <summary>
    /// Gets whether there is an active connection.
    /// </summary>
    bool IsConnected { get; }
}

/// <summary>
/// Represents a server-side transport that accepts incoming connections.
/// </summary>
public interface IServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Starts listening for connections.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Accepts an incoming connection.
    /// </summary>
    Task<IConnection> AcceptAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops listening for connections.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
}
