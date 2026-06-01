namespace ShaRPC.Core;

/// <summary>
/// ShaRPC-defined remote error type names.
/// </summary>
public static class RpcErrorTypes
{
    /// <summary>Remote type name used when an internal service failure is hidden from the caller.</summary>
    public const string InternalError = "ShaRpcInternalError";

    /// <summary>Remote type name used when a peer rejects all inbound calls by configuration.</summary>
    public const string InboundRejected = "ShaRpcInboundRejected";
}
