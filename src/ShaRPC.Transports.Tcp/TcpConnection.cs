using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP-based connection implementation.
/// </summary>
public sealed class TcpConnection : IConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public TcpConnection(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
    }

    public bool IsConnected => _client.Connected && !_disposed;

    public string RemoteEndpoint => _client.Client.RemoteEndPoint?.ToString() ?? "unknown";

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(data, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<Memory<byte>> ReceiveAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpConnection));
        }

        // Read length prefix (4 bytes)
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            var bytesRead = await ReadExactAsync(_stream, lengthBuffer.AsMemory(0, 4), ct);
            if (bytesRead < 4)
            {
                return Memory<byte>.Empty; // Connection closed
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer.AsSpan(0, 4));
            if (totalLength <= 0 || totalLength > 16 * 1024 * 1024) // Max 16MB
            {
                throw new InvalidOperationException($"Invalid message length: {totalLength}");
            }

            // Read the full message (including the length we already read as part of the frame)
            var messageBuffer = new byte[totalLength];
            BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.AsSpan(0, 4), totalLength);

            if (totalLength > 4)
            {
                bytesRead = await ReadExactAsync(_stream, messageBuffer.AsMemory(4), ct);
                if (bytesRead < totalLength - 4)
                {
                    return Memory<byte>.Empty; // Connection closed
                }
            }

            return messageBuffer;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (read == 0)
            {
                return totalRead; // Connection closed
            }
            totalRead += read;
        }
        return totalRead;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stream.Close();
            _client.Close();
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _sendLock.Dispose();
        await Task.CompletedTask;
    }
}
