using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Client;

internal static class ShaRpcClientFrameHelpers
{
    public static RpcRequest CreateEnvelope(
        int messageId,
        string service,
        string method,
        string? instanceId) =>
        new()
        {
            MessageId = messageId,
            ServiceName = service,
            MethodName = method,
            InstanceId = instanceId,
        };

    public static async Task SendCancelFrameAsync(IConnection connection, int messageId)
    {
        try
        {
            using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
            await connection.SendAsync(frame.Memory, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation is best-effort; the request may already have completed or the connection closed.
        }
    }
}
