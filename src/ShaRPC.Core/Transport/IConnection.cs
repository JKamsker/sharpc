namespace ShaRPC.Core.Transport;

/// <summary>
/// Represents a bidirectional connection for sending and receiving data.
/// </summary>
public interface IConnection : IAsyncDisposable
{
    /// <summary>
    /// Sends data over the connection.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Receives data from the connection.
    /// </summary>
    Task<Memory<byte>> ReceiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets whether the connection is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a string representation of the remote endpoint.
    /// </summary>
    string RemoteEndpoint { get; }
}
