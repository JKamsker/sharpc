namespace ShaRPC.Core.Transport;

/// <summary>
/// Explains why a routed duplex frame was not queued for its target side.
/// </summary>
public enum ShaRpcFrameDropReason
{
    /// <summary>
    /// The target side's bounded queue was full and its policy is to drop incoming frames.
    /// </summary>
    QueueFull = 0,

    /// <summary>
    /// The target side had already been closed.
    /// </summary>
    TargetClosed = 1,
}
