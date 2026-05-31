namespace ShaRPC.Core.Transport;

/// <summary>
/// Describes a duplex connection read loop ending.
/// </summary>
public sealed class ShaRpcConnectionClosedEventArgs : EventArgs
{
    public ShaRpcConnectionClosedEventArgs(string remoteEndpoint, Exception? exception)
    {
        RemoteEndpoint = remoteEndpoint;
        Exception = exception;
    }

    /// <summary>
    /// Gets the remote endpoint reported by the underlying connection.
    /// </summary>
    public string RemoteEndpoint { get; }

    /// <summary>
    /// Gets the read-loop exception, or null when the remote side closed cleanly.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets whether the connection ended without a read-loop exception.
    /// </summary>
    public bool IsGraceful => Exception is null;
}
