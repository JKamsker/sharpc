# Changelog

## Unreleased

- **BREAKING:** Removed the legacy `ShaRpcClient`, `ShaRpcServer` (and their builders /
  `IShaRpcClient` / `IShaRpcServer`), `ShaRpcPeer`, and `DuplexConnectionSplitter`. `RpcPeer`
  and `RpcHost` are now the only surface. The wire format is unchanged, so peers remain
  interoperable across versions — only the .NET API changed. Migrate
  `client.CreateXProxy()` → `peer.GetX()`, `serverBuilder.AddX(impl)` →
  `host.ForEachPeer(p => p.ProvideX(impl))`, and `ShaRpcPeer` → `RpcPeer`.
- **BREAKING:** The generated `Create…Proxy(IShaRpcClient)` and `Add…(ShaRpcServerBuilder)`
  extension methods were removed; the generator now emits only `Provide…(RpcPeer)` and
  `Get…(RpcPeer)`. The generated `ShaRpcGenerated.CreateProxy` factory now takes
  `IRpcInvoker` instead of `IShaRpcClient`.
- Added `RpcPeerOptions.MaxConcurrentInboundDispatch` (default 1) for bounded-concurrent
  inbound dispatch per connection: the default dispatches serially, and raising it admits up
  to that many concurrent dispatches while total in-flight inbound work stays bounded by
  `InboundQueueCapacity` + this value.
- Peer wait-mode inbound queues now bound retained request frames instead of staging
  excess requests in an unbounded intake queue.
- TCP tests and callers can bind `TcpServerTransport` to port `0` and read the assigned
  port from `LocalEndpoint` after start.
- `RpcPeerOptions.InboundQueueCapacity` docs now call out that `null` means an unbounded
  queue and should be reserved for trusted or externally bounded peers.
- Server-side exceptions that are not `ShaRpcException` now return a sanitized
  `Internal error.` / `ShaRpcInternalError` error payload instead of exposing the raw
  exception message and CLR exception type to remote callers.
