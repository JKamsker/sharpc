using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcHostLifecycleRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task DisposeDuringStart_DoesNotStartAcceptLoopAfterDispose()
    {
        var transport = new DelayedStartServerTransport();
        await using var host = RpcHost.Listen(transport, NewSerializer());

        var startTask = host.StartAsync();
        await transport.StartEntered.WaitAsync(TimeSpan.FromSeconds(1));

        await host.DisposeAsync();
        transport.AllowStart();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => startTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, transport.AcceptCalls);
        Assert.True(transport.StopCalls > 0);
    }

    [Fact]
    public async Task StopAsync_AfterCanceledTransportStop_CanRetryAndClosePeers()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var transport = new CancelFirstStopServerTransport(serverConnection);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost.Listen(transport, NewSerializer());
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();
        var acceptedPeer = await connected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        using var stopCts = new CancellationTokenSource();
        stopCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.StopAsync(stopCts.Token));
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, transport.StopCalls);
        Assert.False(acceptedPeer.IsConnected);
    }

    [Fact]
    public async Task AcceptedPeerConfigurationFailure_RaisesAcceptError()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var failure = new InvalidOperationException("configuration failed");
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(_ => throw failure);
        host.AcceptError += (_, args) => error.TrySetResult(args.Error);
        await host.StartAsync();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        Assert.Same(failure, await error.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    private sealed class DelayedStartServerTransport : IServerTransport
    {
        private readonly TaskCompletionSource<bool> _startEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acceptCalls;
        private int _stopCalls;

        public Task StartEntered => _startEntered.Task;

        public int AcceptCalls => Volatile.Read(ref _acceptCalls);

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public async Task StartAsync(CancellationToken ct = default)
        {
            _startEntered.TrySetResult(true);
            await _allowStart.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public Task<IConnection> AcceptAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acceptCalls);
            throw new InvalidOperationException("The accept loop should not start.");
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _stopCalls);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;

        public void AllowStart() => _allowStart.TrySetResult(true);
    }

    private sealed class CancelFirstStopServerTransport : IServerTransport
    {
        private readonly IConnection _connection;
        private int _accepted;
        private int _stopCalls;

        public CancelFirstStopServerTransport(IConnection connection) => _connection = connection;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IConnection> AcceptAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                return _connection;
            }

            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _stopCalls);
            return call == 1
                ? Task.Delay(Timeout.Infinite, ct)
                : Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }
}
