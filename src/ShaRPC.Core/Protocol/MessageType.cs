namespace ShaRPC.Core.Protocol;

/// <summary>
/// Defines the types of messages in the ShaRPC protocol.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// An RPC request from client to server.
    /// </summary>
    Request = 0x01,

    /// <summary>
    /// A successful response from server to client.
    /// </summary>
    Response = 0x02,

    /// <summary>
    /// An error response from server to client.
    /// </summary>
    Error = 0x03,

    /// <summary>
    /// A cancellation request (reserved for future use).
    /// </summary>
    Cancel = 0x04
}
