using Shared;
using ShaRPC.Core.Client;
using ShaRPC.Core.Server;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Runs the real <see cref="ShaRpcServer"/> + <see cref="ShaRpcClient"/> — and therefore the
/// generated GameService proxy and dispatcher they drive — over the in-memory
/// <see cref="System.IO.Pipelines.Pipe"/> transport. This proves the generated code round-trips
/// through the full framing/transport stack (RpcRequest/RpcResponse envelope, MessageFramer, the
/// receive loops, MessageId correlation) without sockets, including when the byte stream is
/// delivered one byte at a time.
/// </summary>
public sealed class PipeTransportIntegrationTests
{
    private static async Task<Harness> StartAsync(int writeChunkSize)
    {
        var (clientTransport, serverTransport) = InMemoryPipe.CreatePair(writeChunkSize);
        var serializer = new MessagePackRpcSerializer();

        var server = new ShaRpcServerBuilder()
            .UseTransport(serverTransport)
            .UseSerializer(serializer)
            .AddGameService(new TestGameService())
            .Build();
        await server.StartAsync();

        var client = new ShaRpcClientBuilder()
            .UseTransport(clientTransport)
            .UseSerializer(serializer)
            .WithTimeout(TimeSpan.FromSeconds(5))
            .Build();
        await client.ConnectAsync();

        return new Harness(server, client, client.CreateGameServiceProxy());
    }

    [Fact]
    public async Task GeneratedProxy_RoundTripsEveryMethodShape_OverPipe()
    {
        var h = await StartAsync(writeChunkSize: 0);
        try
        {
            // 0-arg + return
            var status = await h.Game.GetServerStatusAsync();
            Assert.Equal("1.0.0-test", status.Version);

            // string arg + DTO return
            var player = await h.Game.RegisterPlayerAsync("Pipe");
            Assert.Equal("Pipe", player.Name);
            Assert.Equal(100, player.Health);
            Assert.NotEmpty(player.PlayerId);

            // DTO arg + DTO return, with observable server-side mutation
            var moved = await h.Game.MovePlayerAsync(new MoveRequest
            {
                PlayerId = player.PlayerId,
                X = 1,
                Y = 2,
                Z = 3
            });
            Assert.True(moved.Success);

            var fetched = await h.Game.GetPlayerStateAsync(new PlayerId { Id = player.PlayerId });
            Assert.Equal(player.PlayerId, fetched.PlayerId);
            Assert.Equal(1, fetched.PositionX);
            Assert.Equal(2, fetched.PositionY);
            Assert.Equal(3, fetched.PositionZ);

            var action = await h.Game.PerformActionAsync(new ActionRequest
            {
                PlayerId = player.PlayerId,
                ActionType = "Jump"
            });
            Assert.True(action.Success);
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Fact]
    public async Task GeneratedProxy_RoundTrips_OverByteFragmentedStream()
    {
        // 1-byte writes force MessageFramer + the connection's read loop to reassemble every frame
        // from many partial reads — the realistic "the stream delivers a little at a time" case
        // that an in-process direct call never exercises.
        var h = await StartAsync(writeChunkSize: 1);
        try
        {
            var player = await h.Game.RegisterPlayerAsync("Fragmented");
            Assert.Equal("Fragmented", player.Name);

            var status = await h.Game.GetServerStatusAsync();
            Assert.Equal("1.0.0-test", status.Version);

            var fetched = await h.Game.GetPlayerStateAsync(new PlayerId { Id = player.PlayerId });
            Assert.Equal(player.PlayerId, fetched.PlayerId);
            Assert.Equal("Fragmented", fetched.Name);
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentCalls_OverPipe_AllRoundTrip()
    {
        var h = await StartAsync(writeChunkSize: 0);
        try
        {
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => h.Game.GetServerStatusAsync())
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.All(results, status => Assert.Equal("1.0.0-test", status.Version));
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ShaRpcServer _server;
        private readonly ShaRpcClient _client;

        public IGameService Game { get; }

        public Harness(ShaRpcServer server, ShaRpcClient client, IGameService game)
        {
            _server = server;
            _client = client;
            Game = game;
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
    }
}
