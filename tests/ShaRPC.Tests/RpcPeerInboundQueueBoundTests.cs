using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcPeerInboundQueueBoundTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task WaitQueue_DoesNotReadPastConfiguredCapacity()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        for (var id = 1; id <= 4; id++)
        {
            connection.Enqueue(CreateRequestFrame(serializer, id));
        }

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(3, TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.Equal(3, connection.ReceiveCount);

        dispatcher.Release();
        await connection.WaitForReceiveCountAsync(4, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DropIncoming_ReleasesDroppedFrame()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();
        var first = CreateRequestFrame(serializer, 1);
        var second = CreateRequestFrame(serializer, 2);
        var third = CreateRequestFrame(serializer, 3);
        connection.Enqueue(first);
        connection.Enqueue(second);
        connection.Enqueue(third);

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(3, TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveAttemptAsync(4, TimeSpan.FromSeconds(1));
        Assert.True(IsDisposed(second) || IsDisposed(third));

        dispatcher.Release();
    }

    [Fact]
    public async Task RejectInboundCalls_ReturnsExplicitErrorResponse()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    RejectInboundCalls = true,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 42);
        await client.SendAsync(requestFrame.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out var envelope,
            out var payload));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(42, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.Equal(0, payload.Length);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.InboundRejected, response.ErrorType);
        Assert.Equal("This peer does not accept inbound calls.", response.ErrorMessage);
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = BlockingDispatcher.Service,
                MethodName = "Hold",
            },
            ReadOnlySpan<byte>.Empty);

    private static bool IsDisposed(Payload frame)
    {
        try
        {
            _ = frame.Memory;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            _firstEntered.TrySetResult(true);
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }

}
