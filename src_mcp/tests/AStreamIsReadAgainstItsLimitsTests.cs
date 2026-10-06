using System.Text;
using CoaiMcp.Api;
using CoaiMcp.Core.Api;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Reading a streamed answer off the wire (todo/PLAN_api_streaming.md): a character split between two network reads
/// survives; one line without an end is refused; the ANSWER is capped, not the raw stream, which is several times the
/// answer; a connection that drops and a deadline that passes are told apart from an answer — and each keeps what had
/// arrived, so the shim can say how far it got.
/// </summary>
public sealed class AStreamIsReadAgainstItsLimitsTests
{
    private const string Done = "data: [DONE]\n\n";

    private static string Chunk(string content, string finish = "null") =>
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content, ChunkJson.Default.String)
        + "},\"finish_reason\":" + finish + "}]}\n\n";

    private static Task<StreamRead> ReadAsync(Stream body, long maxAnswer = 1_000_000, int maxLine = 100_000, CancellationToken token = default) =>
        StreamedBody.ReadAsync(new StreamContent(body), maxAnswer, maxLine, token);

    [Fact]
    public async Task A_character_split_between_two_reads_arrives_whole()
    {
        var bytes = Encoding.UTF8.GetBytes(Chunk("café 日本", "\"stop\"") + Done);

        var read = await ReadAsync(new Trickle(bytes, 1));

        read.Stop.Should().Be(StreamStop.Ended);
        CompletionReader.Read(read.Outcome.Completion).Content.Should().Be("café 日本");
    }

    [Fact]
    public async Task One_line_with_no_end_is_refused_at_its_ceiling()
    {
        var read = await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("data: " + new string('x', 500))), maxLine: 100);

        read.Stop.Should().Be(StreamStop.LineTooLong);
    }

    [Fact]
    public async Task The_answer_is_capped_not_the_stream_around_it()
    {
        // Twenty characters of answer in a stream many times that size: the cap counts the answer.
        var sse = string.Concat(Enumerable.Repeat(Chunk("ab"), 10)) + Chunk(string.Empty, "\"stop\"") + Done;

        (await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(sse)), maxAnswer: 20)).Stop.Should().Be(StreamStop.Ended);
        (await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(sse)), maxAnswer: 19)).Stop.Should().Be(StreamStop.Capped);
    }

    [Fact]
    public async Task A_connection_that_drops_is_a_drop_and_keeps_what_had_arrived()
    {
        var read = await ReadAsync(new Trickle(Encoding.UTF8.GetBytes(Chunk("half an ")), 64, failAtEnd: true));

        read.Stop.Should().Be(StreamStop.Dropped);
        read.Outcome.ContentChars.Should().Be("half an ".Length);
    }

    [Fact]
    public async Task A_deadline_that_passes_mid_stream_is_a_timeout_and_keeps_what_had_arrived()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var read = await ReadAsync(new Trickle(Encoding.UTF8.GetBytes(Chunk("thinking out loud")), 64, hangAtEnd: true), token: deadline.Token);

        read.Stop.Should().Be(StreamStop.TimedOut);
        read.Outcome.ContentChars.Should().Be("thinking out loud".Length);
    }

    /// <summary>A body that hands out its bytes a few at a time — and then ends, fails, or never answers again.</summary>
    private sealed class Trickle(byte[] bytes, int step, bool failAtEnd = false, bool hangAtEnd = false) : Stream
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
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class ChunkJson : System.Text.Json.Serialization.JsonSerializerContext;
