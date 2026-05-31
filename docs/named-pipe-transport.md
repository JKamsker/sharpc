# Named Pipe Transport

`ShaRPC.Transports.NamedPipes` provides first-class named-pipe client and server
transports for local process-boundary IPC. It is a separate package so hosts that only
need TCP or custom transports do not take a named-pipe dependency.

## Install

```sh
dotnet add package ShaRPC.Transports.NamedPipes
```

The package depends on `ShaRPC` and reuses `StreamConnection` for framing. That means
named-pipe traffic uses the same length validation, serialized sends, pooled receive
buffers, and clean EOF behavior as every other stream-backed ShaRPC connection.

## Server And Client

```csharp
using ShaRPC.Core.Client;
using ShaRPC.Core.Server;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;

var pipeName = "my-plugin-host";

await using var server = new ShaRpcServerBuilder()
    .UseTransport(new NamedPipeServerTransport(pipeName))
    .UseSerializer(new MessagePackRpcSerializer())
    .AddPluginHost(new PluginHost())
    .Build();
await server.StartAsync();

await using var client = new ShaRpcClientBuilder()
    .UseTransport(new NamedPipeClientTransport(pipeName))
    .UseSerializer(new MessagePackRpcSerializer())
    .WithTimeout(TimeSpan.FromSeconds(5))
    .Build();
await client.ConnectAsync();

var host = client.CreatePluginHostProxy();
await host.PingAsync();
```

Use the `(serverName, pipeName)` client constructor when connecting to a remote Windows
machine:

```csharp
var transport = new NamedPipeClientTransport("build-agent-01", "my-plugin-host");
```

## Duplex Peers

Named-pipe streams are duplex, so they can also back `ShaRpcPeer` when both processes
need to serve and call services over the same authenticated pipe. Create or accept a
pipe stream, wrap it in `StreamConnection`, then pass that connection to the peer:

```csharp
using ShaRPC.Core.Peer;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;

Stream pipeStream = /* connected PipeStream */;
await using var connection = new StreamConnection(pipeStream, "pipe://plugin-host");

await using var peer = await ShaRpcPeer.StartAsync(
    connection,
    new MessagePackRpcSerializer(),
    builder => builder.AddDispatcher(
        ShaRpcGenerated.CreateDispatcher<IPluginHost>(new PluginHost())),
    new ShaRpcPeerOptions
    {
        RequestTimeout = TimeSpan.FromSeconds(5),
        InboundQueueCapacity = 256,
        QueueFullMode = ShaRpcQueueFullMode.Wait,
    });

var plugin = peer.CreateProxy<IPluginCallbacks>();
```

`ShaRpcPeer.ConnectionClosed` reports endpoint and read-error details. `FrameDropped`
reports when a bounded peer queue rejects a frame because of queue pressure or because
the target side was already closed.
