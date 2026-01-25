using ShaRPC.Core.Protocol;
using Xunit;

namespace ShaRPC.Tests;

public class MessageFramerTests
{
    [Fact]
    public void Frame_ShouldCreateValidFrame()
    {
        // Arrange
        var messageId = 42;
        var type = MessageType.Request;
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var frame = MessageFramer.Frame(messageId, type, payload);

        // Assert
        Assert.Equal(MessageFramer.HeaderSize + payload.Length, frame.Length);

        // Check total length (first 4 bytes)
        var totalLength = BitConverter.ToInt32(frame, 0);
        Assert.Equal(frame.Length, totalLength);

        // Check message ID (next 4 bytes)
        var extractedMessageId = BitConverter.ToInt32(frame, 4);
        Assert.Equal(messageId, extractedMessageId);

        // Check message type (next 1 byte)
        Assert.Equal((byte)type, frame[8]);

        // Check payload
        Assert.Equal(payload, frame.Skip(MessageFramer.HeaderSize).ToArray());
    }

    [Fact]
    public void Frame_WithEmptyPayload_ShouldCreateValidFrame()
    {
        // Arrange
        var messageId = 1;
        var type = MessageType.Response;
        var payload = Array.Empty<byte>();

        // Act
        var frame = MessageFramer.Frame(messageId, type, payload);

        // Assert
        Assert.Equal(MessageFramer.HeaderSize, frame.Length);
    }

    [Fact]
    public async Task ReadMessageAsync_ShouldReadValidMessage()
    {
        // Arrange
        var messageId = 123;
        var type = MessageType.Request;
        var payload = new byte[] { 10, 20, 30 };
        var frame = MessageFramer.Frame(messageId, type, payload);
        using var stream = new MemoryStream(frame);

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(messageId, result.Value.MessageId);
        Assert.Equal(type, result.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Fact]
    public async Task ReadMessageAsync_WithEmptyStream_ShouldReturnNull()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteMessageAsync_ShouldWriteValidFrame()
    {
        // Arrange
        var messageId = 456;
        var type = MessageType.Error;
        var payload = new byte[] { 100, 200 };
        using var stream = new MemoryStream();

        // Act
        await MessageFramer.WriteMessageAsync(stream, messageId, type, payload);
        stream.Position = 0;
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(messageId, result.Value.MessageId);
        Assert.Equal(type, result.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    [InlineData(MessageType.Cancel)]
    public async Task RoundTrip_ShouldPreserveMessageType(MessageType type)
    {
        // Arrange
        var messageId = 789;
        var payload = new byte[] { 1 };
        var frame = MessageFramer.Frame(messageId, type, payload);
        using var stream = new MemoryStream(frame);

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(type, result.Value.Type);
    }

    [Fact]
    public async Task ReadMessageAsync_WithLargePayload_ShouldSucceed()
    {
        // Arrange
        var messageId = 1;
        var type = MessageType.Request;
        var payload = new byte[10000];
        new Random(42).NextBytes(payload);
        var frame = MessageFramer.Frame(messageId, type, payload);
        using var stream = new MemoryStream(frame);

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(payload, result.Value.Payload);
    }
}
