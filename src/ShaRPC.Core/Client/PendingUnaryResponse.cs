using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core.Client;

internal sealed class PendingUnaryResponse<TResponse> :
    TaskCompletionSource<TResponse>,
    IPendingResponse
{
    private readonly ShaRpcPendingRequests _owner;
    private long _timeoutDeadline = long.MaxValue;
    private int _cancellationKind;

    public PendingUnaryResponse(ShaRpcPendingRequests owner, int messageId)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _owner = owner;
        MessageId = messageId;
    }

    public int MessageId { get; }

    public long TimeoutDeadline => Volatile.Read(ref _timeoutDeadline);

    public PendingCancellationKind CancellationKind =>
        (PendingCancellationKind)Volatile.Read(ref _cancellationKind);

    public bool RegistersStreamingResponse => false;

    public void SetTimeoutDeadline(long deadline) =>
        Volatile.Write(ref _timeoutDeadline, deadline);

    public void CancelByCaller() =>
        _owner.TryCancel(MessageId, this, PendingCancellationKind.Caller);

    public void DisposeResultWhenAvailable()
    {
    }

    public void SetError(Exception error) =>
        TrySetException(error);

    public bool TrySetResponse(
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame,
        RpcStreamReceiver? stream,
        ISerializer serializer)
    {
        try
        {
            if (!response.IsSuccess)
            {
                throw new ShaRpcRemoteException(
                    response.ErrorMessage ?? "Unknown error",
                    response.ErrorType ?? "Unknown");
            }

            if (response.Stream is not null)
            {
                throw new ShaRpcProtocolException(
                    "Response opened a stream for a non-streaming invocation.");
            }

            TrySetResult(serializer.Deserialize<TResponse>(payload));
        }
        catch (Exception ex)
        {
            TrySetException(ex);
        }
        finally
        {
            stream?.Cancel();
            frame.Dispose();
        }

        return true;
    }

    public void TrySetCanceled(PendingCancellationKind kind)
    {
        Volatile.Write(ref _cancellationKind, (int)kind);
        TrySetCanceled();
    }
}
