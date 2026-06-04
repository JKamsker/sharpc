using System.Threading.Channels;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// ROUND 3 / Defect #1 (MEDIUM): a protocol-error reply send failure must not be misattributed as a
/// read-loop fault.
///
/// When <see cref="RpcPeer"/> receives a malformed Request frame, the inbound dispatcher reports a
/// <c>ProtocolError</c> and tries to send a protocol-error reply frame. The queue-full reply send
/// (<c>SendQueueFullErrorAsync</c>) wraps its send in a best-effort try/catch, but the protocol-error
/// reply send in <c>AcceptRequestAsync</c> does not. If that send throws a non-cancellation exception
/// (e.g. a <see cref="ShaRpcConnectionException"/> from a concurrently torn-down transport), it
/// propagates out of the read loop and is reported via the <c>ReadError</c> event — misattributing a
/// SEND failure as a READ error.
///
/// This test drives that exact path with a scripted channel whose <c>SendAsync</c> always throws a
/// <see cref="ShaRpcConnectionException"/> and whose <c>ReceiveAsync</c> yields one malformed Request
/// frame followed by a clean close. The correct behaviour is that the send failure is swallowed
/// (best-effort), so <c>ReadError</c> does NOT fire carrying the send exception. On the current
/// (unfixed) code the send exception surfaces through <c>ReadError</c>, so this test is RED.
/// </summary>
public sealed class Round3_ProtocolErrorSendFailureTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task MalformedRequest_WhenProtocolErrorSendFails_DoesNotSurfaceSendFailureAsReadError()
    {
        var serializer = new MessagePackRpcSerializer();
        await using var connection = new SendThrowingConnection();

        // A Request frame carrying only the 9-byte header (no envelope) passes the frame-processor
        // header check but fails the inbound reader's MessageFramer.TryReadFrame, producing a non-null
        // protocol error. The dispatcher then tries to send a protocol-error reply frame — which this
        // channel fails by throwing ShaRpcConnectionException, simulating a concurrently torn-down
        // transport. The subsequent empty frame is a clean remote close.
        using var malformedRequest = MessageFramer.FrameToPayload(
            42, MessageType.Request, ReadOnlySpan<byte>.Empty);
        connection.Enqueue(CopyFrame(malformedRequest));

        Exception? readErrorRaised = null;
        var protocolErrorCount = 0;
        var disconnected = new TaskCompletionSource<RpcDisconnectedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var peer = RpcPeer.Over(connection, serializer);
        peer.ReadError += (_, args) => readErrorRaised = args.Error;
        peer.ProtocolError += (_, _) => Interlocked.Increment(ref protocolErrorCount);
        peer.Disconnected += (_, args) => disconnected.TrySetResult(args);
        peer.Start();

        // The read loop joins at the Disconnected event in BOTH the fixed and unfixed code, so awaiting
        // it is a deterministic synchronization point with no sleeps.
        var disconnectArgs = await disconnected.Task.WaitAsync(ShortTimeout);

        // The malformed frame should always raise exactly one protocol error.
        Assert.Equal(1, Volatile.Read(ref protocolErrorCount));

        // The defect: the failed protocol-error reply send is reported as a READ error. ReadError is the
        // read-failure channel and must not carry the send-side ShaRpcConnectionException.
        Assert.Null(readErrorRaised);

        // With the send failure correctly swallowed, the loop reaches the clean close, so Disconnected
        // carries no error.
        Assert.Null(disconnectArgs.Error);
    }

    private static Payload CopyFrame(Payload source)
    {
        var copy = Payload.Rent(source.Length);
        source.Memory.Span.CopyTo(copy.Memory.Span);
        return copy;
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> that delivers enqueued inbound frames (then a clean close once
    /// drained) but fails every send by throwing <see cref="ShaRpcConnectionException"/>, mimicking a
    /// transport torn down concurrently while a protocol-error reply is being written.
    /// </summary>
    private sealed class SendThrowingConnection : IRpcChannel
    {
        private readonly Channel<Payload> _inbound =
            Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "send-throwing://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(new ShaRpcConnectionException("Connection lost during send."));

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            // Once the single enqueued frame is consumed, the next read blocks until the channel is
            // completed (on dispose) or returns an empty payload to signal a clean close. Completing the
            // writer here after the last frame keeps the loop deterministic.
            if (_inbound.Reader.TryRead(out var frame))
            {
                _inbound.Writer.TryComplete();
                return frame;
            }

            try
            {
                frame = await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
                return frame;
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }
    }
}
