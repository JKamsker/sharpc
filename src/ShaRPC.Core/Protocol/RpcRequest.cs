namespace ShaRPC.Core.Protocol;

/// <summary>
/// Represents an RPC request message.
/// </summary>
public sealed class RpcRequest
{
    /// <summary>
    /// Unique identifier for this request, used for response correlation.
    /// </summary>
    public int MessageId { get; set; }

    /// <summary>
    /// The name of the service being called.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// The name of the method being called.
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Serialized method arguments.
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
