using System.Globalization;
using System.Text.RegularExpressions;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S4 tests 36–42, 48 and 51: a message belongs to the window of its FIRST line, the gate skips on raw bytes before it decodes,
/// and the budget is charged on what the gate kept.
/// </summary>
/// <remarks>
/// <para>The fixtures span 88 seconds at most, so a bound taken from their own stamps sits INSIDE the 3-hour guard and the gate yields every
/// line. Where a test needs the gate to skip, it uses <see cref="CallerTranscriptFixture.BeyondTheGuard"/>, or moves one line 4 hours later
/// with <c>SetStamp</c> (which re-applies the file's last-write time, so the file is not skipped whole).</para>
/// </remarks>
public sealed partial class TheCallerTranscriptReadsFromTheBoundTests
{
    private const string Prefix = "testsToDelete-coai-bound-";
    private const string C02Agent = "303b2866f0b3022b2";
    private const string C14Agent = "2edf3ddec8861a853";

    [GeneratedRegex("\"timestamp\":\"(?<at>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})")]
    private static partial Regex StampText();

    private static RawAssistant FirstLineOf(CallerTranscriptFixture fixture, string idSuffix) =>
        TranscriptTruth.Assistants(fixture).Where(l => l.MessageId.EndsWith(idSuffix, StringComparison.Ordinal)).OrderBy(l => l.Line).First();

    [Fact]
    public void A_message_straddling_the_bound_is_not_counted_by_its_tail()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var straddler = FirstLineOf(fixture, "8d01ee");
        var from = straddler.Stamp + TimeSpan.FromMilliseconds(500);

        var session = fixture.Read(from);

        session.Messages.Select(m => m.MessageId).Should().NotContain(straddler.MessageId);
        var whole = TranscriptTruth.Assistants(fixture).GroupBy(l => l.MessageId).Where(g => g.Min(l => l.Stamp) >= from).ToList();
        whole.Should().NotBeEmpty();
        session.Messages.Select(m => m.MessageId).Should().BeEquivalentTo(whole.Select(g => g.Key));
        session.Agents.Should().HaveCount(2, "every launch is present whatever the bound");
        foreach (var message in session.Messages)
        {
            message.Tokens.Should().Be(TranscriptTruth.MaxOf(whole.Single(g => g.Key == message.MessageId)));
        }
    }

    [Fact]
    public void A_message_at_or_after_the_bound_is_counted_whole()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var first = FirstLineOf(fixture, "8d01ee");

        var session = fixture.Read(first.Stamp);

        var lines = TranscriptTruth.Assistants(fixture).Where(l => l.MessageId == first.MessageId);
        session.Message(first.MessageId).Tokens.Should().Be(TranscriptTruth.MaxOf(lines));
    }

    [Fact]
    public void A_message_starting_long_before_the_bound_is_not_counted_by_its_tail()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var agent = TranscriptFile.Agent(C02Agent);
        var lines = TranscriptTruth.Assistants(fixture).Where(l => l.MessageId.EndsWith("8d01ee", StringComparison.Ordinal)).OrderBy(l => l.Line).ToList();
        fixture.SetStamp(agent, lines[0].Line, lines[1].Stamp - TimeSpan.FromHours(2.5));
        var from = lines[1].Stamp - TimeSpan.FromSeconds(10);
        fixture.Read().Messages.Select(m => m.MessageId).Should().Contain(lines[0].MessageId, "the id IS a message when nothing bounds the read");

        var session = fixture.Read(from);

        session.Messages.Select(m => m.MessageId).Should().NotContain(lines[0].MessageId, "its first line is inside the 3 h guard, so it is parsed and places the message before the bound");
    }

    [Fact]
    public void An_agent_file_last_written_before_the_bound_yields_its_launch_and_no_messages()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var agent = TranscriptFile.Agent(C02Agent);

        var session = fixture.Read(fixture.LargestStamp() + TimeSpan.FromSeconds(1));

        var found = session.Agent(C02Agent);
        found.LaunchedUtc.Should().Be(fixture.StampOf(agent, 1));
        session.Messages.Should().NotContain(m => m.AgentKey == C02Agent);
        session.Diagnostics.FilesSkippedByLastWrite.Should().Be(1, "the agent file; the main file is always read");
    }

    [Fact]
    public void The_first_line_instant_is_read_under_any_bound()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var unbounded = fixture.Read();

        var session = fixture.Read(fixture.BeyondTheGuard);

        session.FirstLineUtc.Should().NotBeNull().And.Be(unbounded.FirstLineUtc);
        session.Messages.Should().BeEmpty();
        session.Agents.Select(a => a.Key).Should().Equal(unbounded.Agents.Select(a => a.Key));
        session.Diagnostics.LinesSkippedBeforeParse.Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_bound_before_every_line_reads_what_the_unbounded_read_reads()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var unbounded = fixture.Read();
        unbounded.Messages.Should().NotBeEmpty("a bound that changes nothing is only a claim when there is something to change");

        var bounded = fixture.Read(TranscriptTruth.AllStamps(fixture).Min() - TimeSpan.FromDays(1));

        bounded.Should().BeEquivalentTo(unbounded, options => options.WithStrictOrdering().Excluding(s => s.Diagnostics));
    }

    [Fact]
    public void Agents_and_meta_launches_survive_any_bound_and_a_gated_call_does_not()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("missing-child-transcripts", dir);
        var all = fixture.Read();

        var insideTheGuard = fixture.Read(fixture.LargestStamp() + TimeSpan.FromSeconds(1));
        var beyond = fixture.Read(fixture.BeyondTheGuard);

        insideTheGuard.UnobservedLaunches.Should().HaveCount(2);
        insideTheGuard.Agents.Select(a => a.Key).Should().Equal(all.Agents.Select(a => a.Key));
        beyond.Agents.Select(a => a.Key).Should().Equal(all.Agents.Select(a => a.Key));
        beyond.UnobservedLaunches.Should().ContainSingle().Which.Reason.Should().Be(ObservationReasons.MetaWithoutTranscript);
        beyond.State.Render().Should().Be("partial: meta without transcript", "the call-derived launch sat in a line the gate skipped: the stated limit of a bound");
    }

    [Fact]
    public void A_stampless_first_line_does_not_hide_the_first_instant()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("message-as-several-lines", dir);
        var unmutated = fixture.Read(fixture.BeyondTheGuard);
        fixture.PrependLine(TranscriptFile.Main, "{\"type\":\"ai-title\"}");

        var session = fixture.Read(fixture.BeyondTheGuard);

        session.FirstLineUtc.Should().NotBeNull().And.Be(unmutated.FirstLineUtc);
        session.Diagnostics.LinesSkippedBeforeParse.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_budget_is_charged_on_the_slice_not_on_what_was_skipped()
    {
        using var dir = TempDir.For(Prefix);
        var fixture = CallerTranscriptFixture.Materialise("resumed-agent", dir);
        var agent = TranscriptFile.Agent(C14Agent);
        var last = fixture.Lines(agent).Count;
        fixture.SetStamp(agent, last, fixture.LargestStamp() + TimeSpan.FromHours(4));
        var from = fixture.StampOf(agent, last);
        var sliceBytes = SliceBytes(fixture, from - CallerTranscript.StraddleGuard);
        var maxBytes = sliceBytes + 1;

        var session = fixture.Read(new ReadBudget(maxBytes, TimeSpan.MaxValue, TimeProvider.System), from);

        session.State.Render().Should().Be("observed");
        session.Messages.Should().ContainSingle().Which.MessageId.Should().Be(TranscriptTruth.Assistants(fixture).Single(l => l.Line == last && l.FileKey == C14Agent).MessageId);
        session.Diagnostics.BytesInSlice.Should().BeLessThanOrEqualTo(maxBytes).And.Be(sliceBytes);
        session.Diagnostics.BytesScanned.Should().BeGreaterThan(maxBytes, "most of both files was scanned and skipped");
    }

    /// <summary>What E3.S1's gate yields over the raw lines: the head, every line with no stamp key, and every line with a stamp at or after the bound.</summary>
    private static long SliceBytes(CallerTranscriptFixture fixture, DateTime bound)
    {
        var boundText = bound.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        return TranscriptTruth.FilesOf(fixture).Sum(file => SliceBytesOf(fixture, file, boundText));
    }

    /// <summary>One file's share of <see cref="SliceBytes"/>: the bytes of every line the gate keeps, each with its own terminator.</summary>
    private static long SliceBytesOf(CallerTranscriptFixture fixture, TranscriptFile file, string boundText)
    {
        var lengths = fixture.ByteLengths(file);
        var lines = fixture.Lines(file);
        long total = 0;
        var inHead = true;
        for (var i = 0; i < lines.Count; i++)
        {
            var hasKey = lines[i].Contains("\"timestamp\":", StringComparison.Ordinal);
            total += Kept(lines[i], inHead, hasKey, boundText) ? lengths[i] : 0;
            inHead = inHead && !hasKey;
        }

        return total;
    }

    /// <summary>The gate's decision for one line: in the head, carrying no stamp key, or stamped at or after the bound.</summary>
    private static bool Kept(string line, bool inHead, bool hasKey, string boundText) =>
        inHead || !hasKey || StampText().Matches(line).Any(m => string.CompareOrdinal(m.Groups["at"].Value, boundText) >= 0);
}
