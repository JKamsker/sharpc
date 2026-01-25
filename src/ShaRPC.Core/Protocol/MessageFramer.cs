using System.Buffers;
using System.Buffers.Binary;

namespace ShaRPC.Core.Protocol;

/// <summary>
/// Handles message framing for the ShaRPC protocol.
/// Format: [4 bytes: Total Length][4 bytes: MessageId][1 byte: MessageType][N bytes: Payload]
/// </summary>
public static class MessageFramer
{
    /// <summary>
    /// Header size: 4 (length) + 4 (messageId) + 1 (type) = 9 bytes
    /// </summary>
    public const int HeaderSize = 9;

    /// <summary>
    /// Maximum message size (16 MB).
    /// </summary>
    public const int MaxMessageSize = 16 * 1024 * 1024;

    /// <summary>
    /// Frames a message with header information.
    /// </summary>
    public static byte[] Frame(int messageId, MessageType type, ReadOnlySpan<byte> payload)
    {
        var totalLength = HeaderSize + payload.Length;
        var buffer = new byte[totalLength];

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), messageId);
        buffer[8] = (byte)type;

        if (payload.Length > 0)
        {
            payload.CopyTo(buffer.AsSpan(HeaderSize));
        }

        return buffer;
    }

    /// <summary>
    /// Reads a framed message from a stream.
    /// </summary>
    public static async Task<(int MessageId, MessageType Type, byte[] Payload)?> ReadMessageAsync(
        Stream stream,
        CancellationToken ct = default)
    {
        var headerBuffer = ArrayPool<byte>.Shared.Rent(HeaderSize);
        try
        {
            var bytesRead = await ReadExactAsync(stream, headerBuffer.AsMemory(0, HeaderSize), ct);
            if (bytesRead < HeaderSize)
            {
                return null; // Connection closed
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(0, 4));
            if (totalLength < HeaderSize || totalLength > MaxMessageSize)
            {
                throw new InvalidOperationException($"Invalid message length: {totalLength}");
            }

            var messageId = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(4, 4));
            var messageType = (MessageType)headerBuffer[8];

            var payloadLength = totalLength - HeaderSize;
            var payload = new byte[payloadLength];

            if (payloadLength > 0)
            {
                bytesRead = await ReadExactAsync(stream, payload.AsMemory(), ct);
                if (bytesRead < payloadLength)
                {
                    return null; // Connection closed
                }
            }

            return (messageId, messageType, payload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    /// <summary>
    /// Writes a framed message to a stream.
    /// </summary>
    public static async Task WriteMessageAsync(
        Stream stream,
        int messageId,
        MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct = default)
    {
        var frame = Frame(messageId, type, payload.Span);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
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
}
