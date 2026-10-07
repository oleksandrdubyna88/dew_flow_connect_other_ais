namespace CoaiMcp.Tests;

/// <summary>
/// A body that hands out its bytes a few at a time — and then ends, fails, or never answers again. Shared by the stream
/// tests: <c>AStreamIsReadAgainstItsLimitsTests</c> reads it directly, and <c>AStreamsUsageIsNeverZeroTests</c> serves it
/// as an HTTP body, because the endpoint stub cannot hold a stream open in the middle.
/// </summary>
internal sealed class Trickle(byte[] bytes, int step, bool failAtEnd = false, bool hangAtEnd = false) : Stream
{
    private int _at;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _at; set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_at < bytes.Length)
        {
            var count = Math.Min(Math.Min(step, buffer.Length), bytes.Length - _at);
            bytes.AsMemory(_at, count).CopyTo(buffer);
            _at += count;

            return count;
        }
        if (failAtEnd)
        {
            throw new IOException("the connection was reset by the peer");
        }
        if (hangAtEnd)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
