using Inventory.Server;
using ShaRPC.Core.Server;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const int Port = 5051;

Console.WriteLine("ShaRPC Inventory Server");
Console.WriteLine("=======================");
Console.WriteLine("Demonstrates the async-sibling and nested-service features.");
Console.WriteLine();

var transport = new TcpServerTransport(Port);
var serializer = new MessagePackRpcSerializer();
var inventoryService = new InventoryService();

// The generator emits Add{ServiceName} extension methods for every [ShaRpcService].
// We only register the root: the sub-service dispatcher is also generated, but the
// framework reaches it automatically through the per-connection instance registry.
var server = new ShaRpcServerBuilder()
    .UseTransport(transport)
    .UseSerializer(serializer)
    .AddInventoryService(inventoryService)
    .AddPlayerInventory(new NullPlayerInventoryPlaceholder()) // satisfies the registry; never called as a singleton
    .Build();

Console.WriteLine($"Listening on port {Port}.");
await server.StartAsync();
Console.WriteLine("Server ready. Press Ctrl+C to stop.");
Console.WriteLine();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutting down...");
}

await server.StopAsync();
await server.DisposeAsync();
Console.WriteLine("Server stopped.");

/// <summary>
/// A no-op IPlayerInventory used only to satisfy the singleton-dispatcher registration
/// (every [ShaRpcService] generates a top-level dispatcher; for sub-services that
/// nothing ever calls as a singleton, you can register any throwing placeholder).
/// The real per-instance work happens through DispatchOnInstanceAsync on the same
/// generated dispatcher class.
/// </summary>
internal sealed class NullPlayerInventoryPlaceholder : Inventory.Shared.IPlayerInventory
{
    public Task<IReadOnlyList<Inventory.Shared.Item>> ListItemsAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("IPlayerInventory is a sub-service; obtain one via IInventoryService.OpenInventoryAsync.");
    public Task<int> AddItemAsync(string itemId, int quantity, CancellationToken ct = default) =>
        throw new InvalidOperationException("IPlayerInventory is a sub-service; obtain one via IInventoryService.OpenInventoryAsync.");
    public Task<int> RemoveItemAsync(string itemId, int quantity, CancellationToken ct = default) =>
        throw new InvalidOperationException("IPlayerInventory is a sub-service; obtain one via IInventoryService.OpenInventoryAsync.");
    public Task<int> GetTotalValueAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("IPlayerInventory is a sub-service; obtain one via IInventoryService.OpenInventoryAsync.");
}
