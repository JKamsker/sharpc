using System.IO.Pipes;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.NamedPipes;

/// <summary>
/// Client transport for connecting to a ShaRPC server over a named pipe.
/// </summary>
public sealed class NamedPipeClientTransport : ITransport
{
    private readonly string _serverName;
    private readonly string _pipeName;
    private readonly int _maxMessageSize;
    private NamedPipeClientStream? _stream;
    private StreamConnection? _connection;
    private int _disposed;

    public NamedPipeClientTransport(string pipeName, int maxMessageSize = MessageFramer.MaxMessageSize)
        : this(".", pipeName, maxMessageSize)
    {
    }

    public NamedPipeClientTransport(
        string serverName,
        string pipeName,
        int maxMessageSize = MessageFramer.MaxMessageSize)
    {
        _serverName = ValidateName(serverName, nameof(serverName));
        _pipeName = ValidateName(pipeName, nameof(pipeName));
        _maxMessageSize = ValidateMaxMessageSize(maxMessageSize);
    }

    public IRpcChannel? Connection => _connection;

    public bool IsConnected => _connection?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_connection is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        var stream = new NamedPipeClientStream(
            _serverName,
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync(ct).ConfigureAwait(false);
            _stream = stream;
            _connection = new StreamConnection(stream, RemoteEndpoint, ownsStream: true, _maxMessageSize);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _stream?.Dispose();
        }
    }

    private string RemoteEndpoint => $"pipe://{_serverName}/{_pipeName}";

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(NamedPipeClientTransport));
        }
    }

    private static string ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", parameterName);
        }

        return value;
    }

    private static int ValidateMaxMessageSize(int maxMessageSize)
    {
        if (maxMessageSize < MessageFramer.HeaderSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageSize),
                maxMessageSize,
                "Maximum message size must be at least the ShaRPC header size.");
        }

        return maxMessageSize;
    }
}
