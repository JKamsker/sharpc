using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Transport;

/// <summary>
/// Describes a duplex frame dropped before it reached the client or server facade queue.
/// </summary>
public sealed class ShaRpcFrameDroppedEventArgs : EventArgs
{
    public ShaRpcFrameDroppedEventArgs(
        string remoteEndpoint,
        int messageId,
        MessageType messageType,
        ShaRpcFrameDropReason reason)
    {
        RemoteEndpoint = remoteEndpoint;
        MessageId = messageId;
        MessageType = messageType;
        Reason = reason;
    }

    /// <summary>
    /// Gets the remote endpoint reported by the underlying connection.
    /// </summary>
    public string RemoteEndpoint { get; }

    /// <summary>
    /// Gets the ShaRPC message id from the dropped frame header.
    /// </summary>
    public int MessageId { get; }

    /// <summary>
    /// Gets the ShaRPC frame type from the dropped frame header.
    /// </summary>
    public MessageType MessageType { get; }

    /// <summary>
    /// Gets why the frame was dropped.
    /// </summary>
    public ShaRpcFrameDropReason Reason { get; }
}
