using System.Collections.Concurrent;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core.Server;

/// <summary>
/// ShaRPC server that accepts connections and dispatches requests to registered services.
/// </summary>
public sealed class ShaRpcServer : IShaRpcServer
{
    private readonly IServerTransport _transport;
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers = new();
    private readonly ConcurrentDictionary<IConnection, Task> _connections = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    public ShaRpcServer(IServerTransport transport, ISerializer serializer)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Registers a service dispatcher.
    /// </summary>
    public void RegisterDispatcher(IServiceDispatcher dispatcher)
    {
        if (!_dispatchers.TryAdd(dispatcher.ServiceName, dispatcher))
        {
            throw new InvalidOperationException($"Service '{dispatcher.ServiceName}' is already registered.");
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_cts != null)
        {
            throw new InvalidOperationException("Server is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _transport.StartAsync(ct);
        _acceptTask = AcceptConnectionsAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Wait for all connections to close
        var connectionTasks = _connections.Values.ToArray();
        if (connectionTasks.Length > 0)
        {
            await Task.WhenAll(connectionTasks);
        }

        await _transport.StopAsync(ct);
        _cts.Dispose();
        _cts = null;
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var connection = await _transport.AcceptAsync(ct);
                var connectionTask = HandleConnectionAsync(connection, ct);
                _connections.TryAdd(connection, connectionTask);

                // Clean up completed connections
                _ = connectionTask.ContinueWith(_ => _connections.TryRemove(connection, out _), TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Log and continue accepting
            }
        }
    }

    private async Task HandleConnectionAsync(IConnection connection, CancellationToken ct)
    {
        try
        {
            while (connection.IsConnected && !ct.IsCancellationRequested)
            {
                var data = await connection.ReceiveAsync(ct);
                if (data.Length == 0)
                {
                    break; // Connection closed
                }

                await ProcessMessageAsync(connection, data, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception)
        {
            // Log error
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private async Task ProcessMessageAsync(IConnection connection, Memory<byte> data, CancellationToken ct)
    {
        using var stream = new MemoryStream(data.ToArray());
        var message = await MessageFramer.ReadMessageAsync(stream, ct);

        if (message == null)
        {
            return;
        }

        var (messageId, messageType, payload) = message.Value;

        if (messageType != MessageType.Request)
        {
            return; // Server only handles requests
        }

        var request = _serializer.Deserialize<RpcRequest>(payload);
        RpcResponse response;

        try
        {
            if (!_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
            {
                response = new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = false,
                    ErrorMessage = $"Service '{request.ServiceName}' not found.",
                    ErrorType = nameof(ShaRpcNotFoundException)
                };
            }
            else
            {
                var result = await dispatcher.DispatchAsync(request.MethodName, request.Payload, _serializer, ct);
                response = new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = true,
                    Payload = result
                };
            }
        }
        catch (Exception ex)
        {
            response = new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                ErrorType = ex.GetType().Name
            };
        }

        var responsePayload = _serializer.Serialize(response);
        var responseType = response.IsSuccess ? MessageType.Response : MessageType.Error;
        var frame = MessageFramer.Frame(messageId, responseType, responsePayload);
        await connection.SendAsync(frame, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        await _transport.DisposeAsync();
    }
}
