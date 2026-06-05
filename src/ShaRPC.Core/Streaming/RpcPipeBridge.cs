using System.IO.Pipelines;

namespace ShaRPC.Core.Streaming;

internal static class RpcPipeBridge
{
    public static Pipe CreateReadablePipe(RpcStreamReceiver receiver, CancellationToken ct)
    {
        var pipe = new Pipe();
        _ = PumpAsync(receiver, pipe.Writer, ct);
        return pipe;
    }

    private static async Task PumpAsync(
        RpcStreamReceiver receiver,
        PipeWriter writer,
        CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var chunk = await receiver.ReadChunkAsync(ct).ConfigureAwait(false);
                if (chunk is null)
                {
                    await writer.CompleteAsync().ConfigureAwait(false);
                    return;
                }

                using (chunk)
                {
                    var memory = writer.GetMemory(chunk.Payload.Length);
                    chunk.Payload.CopyTo(memory);
                    writer.Advance(chunk.Payload.Length);
                    var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                    if (flush.IsCanceled || flush.IsCompleted)
                    {
                        receiver.Cancel();
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex).ConfigureAwait(false);
        }
    }
}
