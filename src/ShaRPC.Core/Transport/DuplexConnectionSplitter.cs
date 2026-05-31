using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core.Transport;

/// <summary>
/// Splits one duplex connection into a server-facing connection for request frames and a
/// client-facing connection for response frames.
/// </summary>
public sealed class DuplexConnectionSplitter : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly DuplexFacadeConnection _serverConnection;
    private readonly DuplexFacadeConnection _clientConnection;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoop;
    private int _started;
    private int _disposed;

    public DuplexConnectionSplitter(
        IConnection connection,
        DuplexConnectionSplitterOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        var effectiveOptions = (options ?? new DuplexConnectionSplitterOptions()).CloneAndValidate();
        _serverConnection = new DuplexFacadeConnection(connection, effectiveOptions);
        _clientConnection = new DuplexFacadeConnection(connection, effectiveOptions);
    }

    /// <summary>
    /// Connection that receives request and cancel frames for server dispatch.
    /// </summary>
    public IConnection ServerConnection => _serverConnection;

    /// <summary>
    /// Connection that receives response and error frames for client requests.
    /// </summary>
    public IConnection ClientConnection => _clientConnection;

    /// <summary>
    /// Gets whether either facade is still connected.
    /// </summary>
    public bool IsConnected => _serverConnection.IsConnected || _clientConnection.IsConnected;

    /// <summary>
    /// Raised when the shared read loop fails with a non-cancellation exception.
    /// </summary>
    public event Action<Exception>? ReadError;

    /// <summary>
    /// Raised when the remote side closes the shared connection.
    /// </summary>
    public event Action? Disconnected;

    /// <summary>
    /// Raised when the shared read loop ends, including the read error when one occurred.
    /// </summary>
    public event EventHandler<ShaRpcConnectionClosedEventArgs>? ConnectionClosed;

    /// <summary>
    /// Raised when a routed frame is dropped because the target facade cannot queue it.
    /// </summary>
    public event EventHandler<ShaRpcFrameDroppedEventArgs>? FrameDropped;

    /// <summary>
    /// Starts routing frames from the shared connection. Calling this more than once is a no-op.
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        Exception? readError = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Payload frame;
                try
                {
                    frame = await _connection.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    readError = ex;
                    break;
                }

                if (frame.Length == 0)
                {
                    frame.Dispose();
                    break;
                }

                if (!TryReadMessageType(frame, out var type))
                {
                    frame.Dispose();
                    continue;
                }

                var target = type switch
                {
                    MessageType.Response or MessageType.Error => _clientConnection,
                    MessageType.Request or MessageType.Cancel => _serverConnection,
                    _ => null,
                };
                if (target is null)
                {
                    frame.Dispose();
                    continue;
                }

                try
                {
                    var enqueueResult = await target.EnqueueAsync(frame, ct).ConfigureAwait(false);
                    if (enqueueResult != DuplexFrameEnqueueResult.Enqueued)
                    {
                        RaiseFrameDropped(frame, enqueueResult);
                        frame.Dispose();
                    }
                }
                catch
                {
                    frame.Dispose();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            readError = ex;
        }
        finally
        {
            _serverConnection.Complete();
            _clientConnection.Complete();

            if (readError is not null && !ct.IsCancellationRequested)
            {
                ReadError?.Invoke(readError);
            }

            if (!ct.IsCancellationRequested)
            {
                ConnectionClosed?.Invoke(
                    this,
                    new ShaRpcConnectionClosedEventArgs(_connection.RemoteEndpoint, readError));
                Disconnected?.Invoke();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        _serverConnection.Complete();
        _clientConnection.Complete();

        await SafeDisposeAsync(_connection).ConfigureAwait(false);

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort shutdown.
            }
        }

        _serverConnection.DrainAndDispose();
        _clientConnection.DrainAndDispose();
        _cts.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(DuplexConnectionSplitter));
        }
    }

    private static bool TryReadMessageType(Payload frame, out MessageType type)
    {
        var result = MessageFramer.TryReadFrameHeader(frame.Memory, out _, out type);
        return result;
    }

    private void RaiseFrameDropped(Payload frame, DuplexFrameEnqueueResult enqueueResult)
    {
        var handler = FrameDropped;
        if (handler is null ||
            !MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out var messageType))
        {
            return;
        }

        var reason = enqueueResult == DuplexFrameEnqueueResult.QueueFull
            ? ShaRpcFrameDropReason.QueueFull
            : ShaRpcFrameDropReason.TargetClosed;
        handler(
            this,
            new ShaRpcFrameDroppedEventArgs(_connection.RemoteEndpoint, messageId, messageType, reason));
    }

    private static async ValueTask SafeDisposeAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort shutdown.
        }
    }

}
