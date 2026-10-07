using System.Text;
using CoaiMcp.Api;
using CoaiMcp.Core.Api;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Reading a streamed answer off the wire (research/PLAN_api_streaming.md): a character split between two network reads
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
    public async Task One_event_of_many_short_lines_with_no_end_is_refused_at_the_line_ceiling()
    {
        // Each line is under the ceiling; the event they make is not, and nothing ever closes it (the code round's
        // findings 4 and 5: an endpoint could otherwise fill the child's memory below every per-line limit).
        var sse = string.Concat(Enumerable.Repeat("data: xxxxxxxxxx\n", 200));

        var read = await ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(sse)), maxLine: 100);

        read.Stop.Should().Be(StreamStop.LineTooLong);
    }

    [Fact]
    public async Task The_stream_ends_at_its_DONE_even_when_the_connection_stays_open()
    {
        // A proxy that keeps the connection after [DONE] — or keeps sending keep-alives — must not turn a whole, paid
        // answer into a timeout (the own review of the code round).
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bytes = Encoding.UTF8.GetBytes(Chunk("all of it", "\"stop\"") + Done + ": keep-alive\n\n");

        var read = await ReadAsync(new Trickle(bytes, 64, hangAtEnd: true), token: deadline.Token);

        read.Stop.Should().Be(StreamStop.Ended);
        read.Outcome.ContentChars.Should().Be("all of it".Length);
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
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class ChunkJson : System.Text.Json.Serialization.JsonSerializerContext;
