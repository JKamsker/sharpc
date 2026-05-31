using System.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RemoteCancellationTests
{
    [Fact]
    public async Task ClientCancellation_CancelsInFlightServerDispatch()
    {
        var (clientTransport, serverTransport) = InMemoryPipe.CreatePair();
        var serializer = new MessagePackRpcSerializer();
        var service = new CancellableService();

        await using var server = new ShaRpcServerBuilder()
            .UseTransport(serverTransport)
            .UseSerializer(serializer)
            .AddDispatcher(service)
            .Build();
        await server.StartAsync();

        await using var client = new ShaRpcClientBuilder()
            .UseTransport(clientTransport)
            .UseSerializer(serializer)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .Build();
        await client.ConnectAsync();

        using var requestCts = new CancellationTokenSource();

        var call = client.InvokeAsync(service.ServiceName, "Wait", requestCts.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        requestCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        await service.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class CancellableService : IServiceDispatcher
    {
        public string ServiceName => "CancelAware";

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            if (method != "Wait")
            {
                throw new InvalidOperationException("Unexpected method: " + method);
            }

            Started.TrySetResult();

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
