using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Client;

/// <summary>
/// Carries a deserialized response envelope together with the zero-copy payload slice and owner frame.
/// </summary>
internal sealed class ReceivedResponse : IDisposable
{
    private Payload? _frame;

    public ReceivedResponse(RpcResponse response, ReadOnlyMemory<byte> payload, Payload frame)
    {
        Response = response;
        Payload = payload;
        _frame = frame;
    }

    public RpcResponse Response { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public void Dispose() => Interlocked.Exchange(ref _frame, null)?.Dispose();

    public static void DisposeWhenAvailable(Task<ReceivedResponse> task)
    {
        if (task.IsCompleted)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                task.Result.Dispose();
            }

            return;
        }

        _ = task.ContinueWith(
            static t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    t.Result.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
