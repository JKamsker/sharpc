using System.Collections.Concurrent;
using System.Threading.Channels;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcPeerInboundRequestQueue
{
    private readonly Channel<RpcPeerInboundRequest> _queue;
    private readonly Func<RpcPeerInboundRequest, Task> _processAsync;
    private readonly Action<RpcPeerInboundRequest> _release;
    private readonly bool _dropIncomingWhenFull;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private CancellationTokenSource? _cts;
    private Task? _dispatchWorker;

    public RpcPeerInboundRequestQueue(
        int capacity,
        ShaRpcQueueFullMode mode,
        int maxConcurrency,
        Func<RpcPeerInboundRequest, Task> processAsync,
        Action<RpcPeerInboundRequest> release)
    {
        _processAsync = processAsync;
        _release = release;
        _dropIncomingWhenFull = mode == ShaRpcQueueFullMode.DropIncoming;
        // maxConcurrency == 1 keeps dispatch strictly serial; > 1 admits that many concurrent
        // dispatches. Total in-flight inbound work is bounded by capacity + maxConcurrency.
        _slots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _queue = Channel.CreateBounded<RpcPeerInboundRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public void Start(CancellationToken loopCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        _dispatchWorker = Task.Run(() => DispatchAsync(_cts.Token));
    }

    public async ValueTask<bool> EnqueueAsync(RpcPeerInboundRequest inbound, CancellationToken ct)
    {
        if (_dropIncomingWhenFull)
        {
            if (_queue.Writer.TryWrite(inbound))
            {
                return true;
            }

            // Release the request resources but leave frame disposal to the read loop, which
            // disposes on the false return. Disposing here too would double-return the pooled
            // buffer (benign only while Payload.Dispose stays idempotent).
            _release(inbound);
            return false;
        }

        try
        {
            await _queue.Writer.WriteAsync(inbound, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _release(inbound);
            return false;
        }
        catch (ChannelClosedException)
        {
            _release(inbound);
            return false;
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _queue.Writer.TryComplete();

        if (_dispatchWorker is not null)
        {
            await ObserveShutdownAsync(_dispatchWorker).ConfigureAwait(false);
        }

        var inFlight = _inFlight.Keys.ToArray();
        if (inFlight.Length != 0)
        {
            await ObserveShutdownAsync(Task.WhenAll(inFlight)).ConfigureAwait(false);
        }

        Drain();
        _slots.Dispose();
        _cts?.Dispose();
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // Acquire a dispatch slot BEFORE pulling the item off the channel, so a blocked
                // dispatcher does not let the worker drain extra items into limbo: at most
                // maxConcurrency items are removed from the channel beyond what is dispatching,
                // keeping read-side backpressure at exactly capacity + maxConcurrency (and
                // identical to inline serial dispatch when maxConcurrency == 1).
                await _slots.WaitAsync(ct).ConfigureAwait(false);
                if (_queue.Reader.TryRead(out var inbound))
                {
                    StartProcessing(inbound);
                }
                else
                {
                    // Writer completed with no item left for this slot; hand it back.
                    _slots.Release();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during peer shutdown.
        }
    }

    private void StartProcessing(RpcPeerInboundRequest inbound)
    {
        var task = ProcessOneAsync(inbound);
        if (task.IsCompleted)
        {
            return;
        }

        // Track in-flight dispatches so StopAsync can await them. Register before attaching the
        // self-removal continuation so a task that completes between the check and TryAdd is still removed.
        _inFlight.TryAdd(task, 0);
        _ = task.ContinueWith(
            static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
            _inFlight,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ProcessOneAsync(RpcPeerInboundRequest inbound)
    {
        try
        {
            await _processAsync(inbound).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Inbound request dispatch failed", ex);
        }
        finally
        {
            try
            {
                _slots.Release();
            }
            catch (ObjectDisposedException)
            {
                // StopAsync disposed the slot semaphore after the dispatch worker stopped.
            }
        }
    }

    private void Drain()
    {
        while (_queue.Reader.TryRead(out var inbound))
        {
            inbound.Frame.Dispose();
            _release(inbound);
        }
    }

    private static async Task ObserveShutdownAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Request dispatch observes its own failures.
        }
    }
}
