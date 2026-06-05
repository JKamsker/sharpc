using System.Threading.Channels;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcStreamReceiver
{
    private readonly Channel<RpcStreamChunk> _chunks;
    private readonly RpcStreamManager _manager;
    private RpcOutboundStreamSet? _outboundStreams;
    private int _completed;

    public RpcStreamReceiver(RpcStreamManager manager, RpcStreamHandle handle)
    {
        _manager = manager;
        Handle = handle;
        _chunks = Channel.CreateBounded<RpcStreamChunk>(
            new BoundedChannelOptions(RpcStreamManager.WindowSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
            });
    }

    public RpcStreamHandle Handle { get; }

    public void AttachOutboundStreams(RpcOutboundStreamSet streams) =>
        _outboundStreams = streams;

    public bool TryAccept(Payload frame)
    {
        if (Volatile.Read(ref _completed) != 0)
        {
            return false;
        }

        var chunk = new RpcStreamChunk(
            this,
            frame,
            frame.Memory.Slice(MessageFramer.HeaderSize));
        if (_chunks.Writer.TryWrite(chunk))
        {
            return true;
        }

        chunk.Dispose();
        Complete(new InvalidDataException("Stream receiver window was exceeded."));
        return false;
    }

    public async ValueTask<RpcStreamChunk?> ReadChunkAsync(CancellationToken ct)
    {
        try
        {
            while (await _chunks.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                if (_chunks.Reader.TryRead(out var chunk))
                {
                    return chunk;
                }
            }

            await _chunks.Reader.Completion.ConfigureAwait(false);
            return null;
        }
        finally
        {
            if (_chunks.Reader.Completion.IsCompleted)
            {
                _manager.RemoveInbound(Handle.StreamId);
            }
        }
    }

    public void Complete(Exception? error = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _chunks.Writer.TryComplete(error);
        if (Interlocked.Exchange(ref _outboundStreams, null) is { } streams)
        {
            _ = streams.DisposeAsync();
        }
    }

    public void Cancel()
    {
        _ = CancelAsync();
    }

    public async ValueTask CancelAsync()
    {
        if (Volatile.Read(ref _completed) != 0)
        {
            _manager.RemoveInbound(Handle.StreamId);
            return;
        }

        await _manager.SendCancelAsync(Handle.StreamId, CancellationToken.None).ConfigureAwait(false);
        Complete(new OperationCanceledException());
        _manager.RemoveInbound(Handle.StreamId);
    }

    public void ReleaseCredit()
    {
        if (Volatile.Read(ref _completed) == 0)
        {
            _ = _manager.SendCreditAsync(Handle.StreamId, 1, CancellationToken.None);
        }
    }
}
