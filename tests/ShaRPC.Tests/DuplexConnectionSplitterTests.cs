using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests;

public class DuplexConnectionSplitterTests
{
    [Fact]
    public async Task RoutesCancelFramesByHeaderWithoutRequiringEnvelope()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var splitter = new DuplexConnectionSplitter(serverConnection);
        splitter.Start();

        using var frame = MessageFramer.FrameToPayload(9, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        await client.SendAsync(frame.Memory);

        using var routed = await splitter.ServerConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(frame.Memory.ToArray(), routed.Memory.ToArray());
    }

    [Fact]
    public async Task ReportsGracefulConnectionClose()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var splitter = new DuplexConnectionSplitter(serverConnection);
        var closed = new TaskCompletionSource<ShaRpcConnectionClosedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        splitter.ConnectionClosed += (_, args) => closed.TrySetResult(args);
        splitter.Start();

        await client.DisposeAsync();

        var eventArgs = await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(eventArgs.IsGraceful);
        Assert.Null(eventArgs.Exception);
        Assert.Equal("memory://client", eventArgs.RemoteEndpoint);
    }

    [Fact]
    public async Task ReportsFrameDroppedWhenBoundedQueueDropsIncoming()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var splitter = new DuplexConnectionSplitter(
            serverConnection,
            new DuplexConnectionSplitterOptions
            {
                QueueCapacity = 1,
                QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
            });
        var dropped = new TaskCompletionSource<ShaRpcFrameDroppedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        splitter.FrameDropped += (_, args) => dropped.TrySetResult(args);
        splitter.Start();

        using var first = MessageFramer.FrameToPayload(1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        using var second = MessageFramer.FrameToPayload(2, MessageType.Request, ReadOnlySpan<byte>.Empty);
        await client.SendAsync(first.Memory);
        await client.SendAsync(second.Memory);

        var eventArgs = await dropped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, eventArgs.MessageId);
        Assert.Equal(MessageType.Request, eventArgs.MessageType);
        Assert.Equal(ShaRpcFrameDropReason.QueueFull, eventArgs.Reason);
        Assert.Equal("memory://client", eventArgs.RemoteEndpoint);
    }
}
