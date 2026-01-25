namespace ShaRPC.Core.Client;

/// <summary>
/// Interface for the ShaRPC client.
/// </summary>
public interface IShaRpcClient : IAsyncDisposable
{
    /// <summary>
    /// Connects to the server.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method.
    /// </summary>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method with no request body.
    /// </summary>
    Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default);

    /// <summary>
    /// Invokes an RPC method with no response body.
    /// </summary>
    Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Gets whether the client is connected.
    /// </summary>
    bool IsConnected { get; }
}
