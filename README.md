# ShaRPC

A high-performance, transport-agnostic RPC framework for C# with source generator-based code generation, designed for Unity and .NET interoperability.

## Features

- **Source Generator Based**: Compile-time proxy and dispatcher generation - no runtime reflection
- **Unity Compatible**: Works with IL2CPP and AOT compilation
- **Transport Agnostic**: TCP included, easily extensible to WebSocket, Steam, etc.
- **Shared Contracts**: Same C# interfaces on client and server
- **Fast Serialization**: MessagePack for efficient binary encoding
- **Async/Await**: Full async support with cancellation tokens

## Quick Start

### 1. Define Service Contract

```csharp
[ShaRpcService]
public interface IGameService
{
    Task<PlayerState> JoinAsync(string playerName, CancellationToken ct = default);
    Task<ActionResult> MoveAsync(MoveRequest request, CancellationToken ct = default);
}
```

### 2. Implement Server

```csharp
var server = new ShaRpcServerBuilder()
    .UseTransport(new TcpServerTransport(7777))
    .UseSerializer(new MessagePackRpcSerializer())
    .AddGameService(new GameService())
    .Build();

await server.StartAsync();
```

### 3. Create Client

```csharp
var client = new ShaRpcClientBuilder()
    .UseTransport(new TcpTransport("localhost", 7777))
    .UseSerializer(new MessagePackRpcSerializer())
    .Build();

await client.ConnectAsync();

var gameService = client.CreateGameServiceProxy();
var player = await gameService.JoinAsync("Player1");
```

## Project Structure

```
sharpc/
├── src/
│   ├── ShaRPC.Core/                    # Core abstractions and infrastructure
│   ├── ShaRPC.SourceGenerator/         # Compile-time code generation
│   ├── ShaRPC.Transports.Tcp/          # TCP transport implementation
│   └── ShaRPC.Serializers.MessagePack/ # MessagePack serialization
├── samples/
│   ├── Shared/                         # Example service definitions
│   ├── Server/                         # Example server
│   └── Client/                         # Example client
├── tests/
│   └── ShaRPC.Tests/                   # Unit and integration tests
└── docs/
    ├── quick-start.md                  # Getting started guide
    ├── unity-integration.md            # Unity integration guide
    └── api-reference.md                # API documentation
```

## Documentation

- [Quick Start Guide](docs/quick-start.md)
- [Unity Integration Guide](docs/unity-integration.md)
- [API Reference](docs/api-reference.md)

## Building

```bash
# Build all projects
dotnet build

# Run tests
dotnet test

# Run sample server
dotnet run --project samples/Server

# Run sample client (in another terminal)
dotnet run --project samples/Client
```

## Requirements

- **.NET Standard 2.1** for library projects (Unity 2021.3+)
- **.NET 6.0+** for server projects (recommended: .NET 8.0+)
- **MessagePack** 2.5.x

## Why ShaRPC?

| Feature               | ShaRPC       | gRPC        | SignalR     | [Dan.Net](https://github.com/danqzq/dan.net)      |
|-----------------------|--------------|-------------|-------------|---------------------------------------------------|
| Unity IL2CPP          | Yes          | Complex     | Limited     | Yes                                               |
| Shared C# contracts   | Yes          | Proto files | Hub methods | Interfaces (`ISyncData` and inheritance patterns) |
| Transport agnostic    | Yes          | HTTP/2 only | WebSocket   | WebSocket only                                    |
| Binary serialization  | MessagePack  | Protobuf    | JSON        | JSON/Binary streams                               |
| Code generation       | Compile-time | Build step  | Runtime     | None                                              |

## License

[MIT](LICENSE)
