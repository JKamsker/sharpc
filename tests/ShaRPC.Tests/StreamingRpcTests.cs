using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingRpcTests
{
    [Fact]
    public async Task AsyncEnumerableResponse_YieldsIncrementally_AndDoesNotBlockOtherCalls()
    {
        var service = new StreamingDispatcher();
        await using var pair = await PeerPair.StartAsync(service);

        await using var enumerator = pair.Client
            .InvokeAsyncEnumerable<int>("Streaming", "Numbers")
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, enumerator.Current);

        var ping = await pair.Client.InvokeAsync<int>("Streaming", "Ping")
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, ping);

        service.ReleaseNumbers();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, enumerator.Current);
        Assert.False(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task StreamResponse_ReadsBytesAsProducerReleasesThem()
    {
        var service = new StreamingDispatcher();
        await using var pair = await PeerPair.StartAsync(service);

        var streamTask = pair.Client.InvokeStreamAsync("Streaming", "Download");
        await service.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = await streamTask.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DownloadSourceRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, read);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);

        var delayedRead = stream.ReadAsync(buffer).AsTask();
        Assert.False(delayedRead.IsCompleted);

        var ping = await pair.Client.InvokeAsync<int>("Streaming", "Ping")
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, ping);

        service.ReleaseDownload();
        read = await delayedRead.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, read);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, buffer);
    }

    [Fact]
    public async Task PipeResponse_CanBeReadThroughPipeReader()
    {
        var service = new StreamingDispatcher();
        await using var pair = await PeerPair.StartAsync(service);

        var pipe = await pair.Client.InvokePipeAsync("Streaming", "Pipe")
            .WaitAsync(TimeSpan.FromSeconds(5));
        var result = await pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new byte[] { 9, 10, 11 }, result.Buffer.ToArray());
        pipe.Reader.AdvanceTo(result.Buffer.End);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task StreamedArguments_AreReadIncrementallyByDispatcher()
    {
        var service = new StreamingDispatcher();
        await using var pair = await PeerPair.StartAsync(service);
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 5, 6 });
        await pipe.Writer.CompleteAsync();

        var bytes = pair.Client.ReserveStream(RpcStreamKind.Binary);
        var items = pair.Client.ReserveStream(RpcStreamKind.Items);
        var pipeHandle = pair.Client.ReserveStream(RpcStreamKind.Binary);
        var request = (bytes, items, pipeHandle);
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(bytes, new MemoryStream(new byte[] { 1, 2, 3 })),
            RpcStreamAttachment.FromAsyncEnumerable(items, UploadItems()),
            RpcStreamAttachment.FromPipe(pipeHandle, pipe),
        };

        var upload = pair.Client
            .InvokeAsync<(RpcStreamHandle, RpcStreamHandle, RpcStreamHandle), int>(
                "Streaming",
                "Upload",
                request,
                attachments);

        await WaitForStageOrUploadAsync(service.UploadStarted.Task, upload, "start");
        await WaitForStageOrUploadAsync(service.UploadBytesRead.Task, upload, "read byte stream");
        await WaitForStageOrUploadAsync(service.UploadItemsRead.Task, upload, "read item stream");
        await WaitForStageOrUploadAsync(service.UploadPipeRead.Task, upload, "read pipe stream");
        var sum = await upload.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1 + 2 + 3 + 10 + 20 + 5 + 6, sum);
    }

    [Fact]
    public async Task StreamManager_DeliversBinaryStreamAfterInitialCredit()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcStreamManager? clientStreams = null;
        RpcStreamManager? serverStreams = null;
        clientStreams = new RpcStreamManager(serializer, SendToServerAsync, exceptionTransformer: null);
        serverStreams = new RpcStreamManager(serializer, SendToClientAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(100, RpcStreamKind.Binary);
        var receiver = serverStreams.GetOrRegisterInbound(handle, CancellationToken.None);

        await using var outbound = clientStreams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 7, 8, 9 })) },
            CancellationToken.None);
        outbound.Start();

        await using var stream = new RpcRemoteStream(receiver);
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer, CancellationToken.None).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await outbound.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 7, 8, 9 }, buffer);

        Task SendToClientAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            var payload = CopyPayload(frame);
            if (!MessageFramer.TryReadFrameHeader(payload.Memory, out var streamId, out var type))
            {
                payload.Dispose();
                throw new InvalidDataException("Malformed test frame.");
            }

            if (type == MessageType.StreamCredit)
            {
                Assert.True(clientStreams!.TryAddCredit(payload));
                payload.Dispose();
                return Task.CompletedTask;
            }

            payload.Dispose();
            throw new InvalidDataException("Unexpected client frame type: " + type);
        }

        Task SendToServerAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            var payload = CopyPayload(frame);
            if (!MessageFramer.TryReadFrameHeader(payload.Memory, out var streamId, out var type))
            {
                payload.Dispose();
                throw new InvalidDataException("Malformed test frame.");
            }

            switch (type)
            {
                case MessageType.StreamItem:
                    if (!serverStreams!.TryAcceptItem(streamId, payload))
                    {
                        payload.Dispose();
                        throw new InvalidDataException("Unknown test stream.");
                    }
                    return Task.CompletedTask;
                case MessageType.StreamComplete:
                    serverStreams!.CompleteInbound(streamId);
                    payload.Dispose();
                    return Task.CompletedTask;
                default:
                    payload.Dispose();
                    throw new InvalidDataException("Unexpected server frame type: " + type);
            }
        }
    }

    [Fact]
    public async Task DisposingAsyncEnumerableResponse_CancelsRemoteProducer()
    {
        var service = new StreamingDispatcher();
        await using var pair = await PeerPair.StartAsync(service);

        var enumerator = pair.Client
            .InvokeAsyncEnumerable<int>("Streaming", "Numbers")
            .GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

        await enumerator.DisposeAsync();

        await service.NumbersCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Payload CopyPayload(ReadOnlyMemory<byte> frame)
    {
        var payload = Payload.Rent(frame.Length);
        frame.CopyTo(payload.Memory);
        return payload;
    }

    private static async Task WaitForStageOrUploadAsync(
        Task stage,
        Task upload,
        string stageName)
    {
        var completed = await Task.WhenAny(stage, upload, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed == stage)
        {
            await stage;
            return;
        }

        if (completed == upload)
        {
            await upload;
            throw new InvalidOperationException("Upload completed before stage: " + stageName);
        }

        throw new TimeoutException("Upload did not reach stage: " + stageName);
    }

    private static async IAsyncEnumerable<int> UploadItems()
    {
        yield return 10;
        await Task.Yield();
        yield return 20;
    }

    private sealed class StreamingDispatcher : IServiceDispatcher
    {
        private readonly TaskCompletionSource _numbersGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _downloadGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => "Streaming";

        public TaskCompletionSource NumbersCanceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource UploadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DownloadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DownloadSourceRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource UploadBytesRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource UploadItemsRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource UploadPipeRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseNumbers() => _numbersGate.TrySetResult();

        public void ReleaseDownload() => _downloadGate.TrySetResult();

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Streaming tests use the streaming dispatch overload.");

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            switch (method)
            {
                case "Numbers":
                    streaming.SetResponse(NumbersAsync(ct));
                    return Task.CompletedTask;
                case "Download":
                    DownloadStarted.TrySetResult();
                    streaming.SetResponse(new GatedStream(_downloadGate.Task, DownloadSourceRead));
                    return Task.CompletedTask;
                case "Pipe":
                    streaming.SetResponse(CreatePipe());
                    return Task.CompletedTask;
                case "Upload":
                    return DispatchUploadAsync(payload, serializer, streaming, output, ct);
                case "Ping":
                    serializer.Serialize(output, 42);
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException("Unexpected method: " + method);
            }
        }

        private async IAsyncEnumerable<int> NumbersAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                yield return 1;
                await _numbersGate.Task.WaitAsync(ct).ConfigureAwait(false);
                yield return 2;
            }
            finally
            {
                NumbersCanceled.TrySetResult();
            }
        }

        private async Task DispatchUploadAsync(
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IRpcStreamingContext streaming,
            IBufferWriter<byte> output,
            CancellationToken ct)
        {
            UploadStarted.TrySetResult();
            var handles = serializer.Deserialize<(RpcStreamHandle, RpcStreamHandle, RpcStreamHandle)>(payload);
            var sum = 0;

            await using (var bytes = streaming.GetStream(handles.Item1))
            {
                var buffer = new byte[16];
                int read;
                while ((read = await bytes.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    sum += buffer.AsSpan(0, read).ToArray().Sum(static b => b);
                }
            }
            UploadBytesRead.TrySetResult();

            await foreach (var item in streaming.GetAsyncEnumerable<int>(handles.Item2).WithCancellation(ct))
            {
                sum += item;
            }
            UploadItemsRead.TrySetResult();

            var pipe = streaming.GetPipe(handles.Item3);
            while (true)
            {
                var result = await pipe.Reader.ReadAsync(ct).ConfigureAwait(false);
                foreach (var segment in result.Buffer)
                {
                    sum += segment.Span.ToArray().Sum(static b => b);
                }

                pipe.Reader.AdvanceTo(result.Buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);
            UploadPipeRead.TrySetResult();

            serializer.Serialize(output, sum);
        }

        private static Pipe CreatePipe()
        {
            var pipe = new Pipe();
            pipe.Writer.Write(new byte[] { 9, 10, 11 });
            _ = pipe.Writer.CompleteAsync();
            return pipe;
        }
    }

    private sealed class GatedStream : Stream
    {
        private readonly Task _gate;
        private int _readCount;

        private readonly TaskCompletionSource _readStarted;

        public GatedStream(Task gate, TaskCompletionSource readStarted)
        {
            _gate = gate;
            _readStarted = readStarted;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = Interlocked.Increment(ref _readCount);
            if (read == 1)
            {
                _readStarted.TrySetResult();
                new byte[] { 1, 2, 3, 4 }.CopyTo(buffer);
                return 4;
            }

            if (read == 2)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                new byte[] { 5, 6, 7, 8 }.CopyTo(buffer);
                return 4;
            }

            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class PeerPair : IAsyncDisposable
    {
        private PeerPair(RpcPeer client, RpcPeer server)
        {
            Client = client;
            Server = server;
        }

        public RpcPeer Client { get; }

        public RpcPeer Server { get; }

        public static Task<PeerPair> StartAsync(IServiceDispatcher dispatcher)
        {
            var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
            var serializer = new MessagePackRpcSerializer();
            var server = RpcPeer
                .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
                .Provide(dispatcher)
                .Start();
            var client = RpcPeer
                .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
                .Start();
            return Task.FromResult(new PeerPair(client, server));
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
