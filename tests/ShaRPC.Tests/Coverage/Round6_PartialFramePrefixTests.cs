using System.IO;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// Round 6 regression for <see cref="StreamConnection.ReceiveAsync"/> (and the identical
/// <c>TcpConnection.ReceiveAsync</c>). When a peer sends 1–3 bytes of the 4-byte frame length prefix and
/// then closes, <c>ReadExactAsync</c> returns the partial count (&lt; 4) and the code returned the
/// <c>Payload.Empty</c> clean-disconnect sentinel — making a framing violation indistinguishable from a
/// clean idle close (no <c>ReadError</c>, <c>Disconnected</c> with a null error). A truncated prefix is a
/// protocol error and must surface as one. (A genuine clean close yields a read count of exactly 0.)
/// </summary>
public sealed class Round6_PartialFramePrefixTests
{
    [Fact]
    public async Task ReceiveAsync_WhenPeerSendsPartialLengthPrefixThenCloses_ThrowsProtocolError()
    {
        // 2 of the 4 length-prefix bytes, then EOF.
        var stream = new ScriptedReadStream(new byte[] { 0x01, 0x00 });
        await using var connection = new StreamConnection(stream, ownsStream: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => connection.ReceiveAsync());
    }

    /// <summary>Delivers a fixed first chunk, then reports EOF (0) on every subsequent read.</summary>
    private sealed class ScriptedReadStream : Stream
    {
        private readonly byte[] _firstChunk;
        private int _offset;

        public ScriptedReadStream(byte[] firstChunk) => _firstChunk = firstChunk;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_offset >= _firstChunk.Length || buffer.Length == 0)
            {
                return 0; // EOF after the partial prefix
            }

            var n = System.Math.Min(buffer.Length, _firstChunk.Length - _offset);
            _firstChunk.AsSpan(_offset, n).CopyTo(buffer.Span);
            _offset += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
