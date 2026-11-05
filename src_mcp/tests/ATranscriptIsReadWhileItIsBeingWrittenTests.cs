using System.Globalization;
using System.Text;
using System.Text.Json;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S1 of <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>: a transcript its CLI is still appending to is
/// read shared, never throws, is parsed without a pre-read of any property, and — for the new reader — lines before the bound
/// are skipped on their raw bytes and never decoded.
/// </summary>
/// <remarks>
/// <para>Every instant here is chosen by the test on the synthetic clock of the E1 fixtures (2000-01-01); nothing reads the real
/// clock or today's date. A file's last-write time is set explicitly where it matters.</para>
/// <para>What these tests CANNOT see: that a skipped line is never decoded. Decoding bad bytes does not throw, it substitutes,
/// so the guarantee is read in the source (one gate, no decode call anywhere in <c>TranscriptLines</c>) and timed by E5.</para>
/// </remarks>
public sealed class ATranscriptIsReadWhileItIsBeingWrittenTests
{
    private static DateTime At(int hour, int minute, int second = 0) => new(2000, 1, 1, hour, minute, second, DateTimeKind.Utc);

    private static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A line shaped like a real one: the top-level stamp comes AFTER <c>message</c> (237 of 240 fixture lines).</summary>
    private static string Stamped(DateTime at, string pad = "") =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"m\",\"pad\":\"" + pad + "\"},\"timestamp\":\"" + Stamp(at) + "\"}";

    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static string Joined(params string[] lines) => string.Join('\n', lines) + "\n";

    private static void Write(string file, string text) => File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(text));

    /// <summary>Decoded in the TEST, one line at a time: the enumerator reuses its buffer on the next MoveNext.</summary>
    private static List<string> Read(string file, DateTime bound, ScanMeter meter) =>
        [.. TranscriptLines.ReadSharedFrom(file, bound, meter).Select(line => Encoding.UTF8.GetString(line.Span))];

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ---- shared read, torn lines, the file-level gate ------------------------------------------------------------------

    [Fact]
    public void A_file_held_open_for_writing_by_another_handle_is_still_read()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-held-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1))];
        using var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write(Utf8(Joined(lines)));
        writer.Flush();

        TranscriptLines.ReadShared(file).Should().Equal(lines, "the CLI keeps its handle while a round reads");
        Read(file, DateTime.MinValue, new ScanMeter()).Should().Equal(lines, "the byte reader opens the same way");
    }

    [Fact]
    public void A_file_that_cannot_be_opened_is_no_lines_not_an_exception()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-open-");
        var meter = new ScanMeter();

        TranscriptLines.ReadShared(dir.At("missing.jsonl")).Should().BeEmpty();
        TranscriptLines.ReadShared(dir.Path).Should().BeEmpty("a directory is not a transcript");
        Read(dir.At("missing.jsonl"), DateTime.MinValue, meter).Should().BeEmpty();
        Read(dir.Path, DateTime.MinValue, meter).Should().BeEmpty();
        meter.ReadFailures.Should().Be(0, "a file that never opened is not a read that failed");
    }

    [Fact]
    public void A_torn_line_is_reported_as_torn_and_the_next_whole_line_still_parses()
    {
        // Real unparseable lines exist (Q9: 1 on Windows, 3 on WSL) but no fixture holds one, so the lines are synthetic.
        var torn = Stamped(At(10, 0))[..30];
        var whole = Stamped(At(10, 1));

        TranscriptLines.TryParse(torn, out var none).Should().BeFalse();
        none.Should().BeNull();
        TranscriptLines.TryParse(whole, out var document).Should().BeTrue();
        using (document)
        {
            document!.RootElement.GetProperty("type").GetString().Should().Be("assistant");
        }

        TranscriptLines.TryParse(Utf8(torn), out _).Should().BeFalse("the byte overload has the same contract");
        TranscriptLines.TryParse(Utf8(whole), out var fromBytes).Should().BeTrue();
        using (fromBytes)
        {
            fromBytes!.RootElement.GetProperty("message").GetProperty("id").GetString().Should().Be("m");
        }
    }

    [Fact]
    public void Only_the_days_a_window_touches_are_listed()
    {
        var opened = At(13, 0);
        var planRound = At(13, 39);

        TranscriptLines.Days(opened, planRound).Should().Equal(["2000-01-01"]);
        TranscriptLines.Days(opened, opened.AddDays(2)).Should().HaveCount(3);
        TranscriptLines.Days(opened, opened.AddDays(30)).Should().BeEmpty("a window that wide is not worth listing, so every line is read");
        TranscriptLines.Mentions(Stamped(opened), ["2000-01-01"]).Should().BeTrue();
        TranscriptLines.Mentions(Stamped(opened), ["2000-01-02"]).Should().BeFalse();
        TranscriptLines.Mentions(Stamped(opened), []).Should().BeTrue("no day list means every line is read");
    }

    [Fact]
    public void A_file_last_written_before_the_bound_is_skipped()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-written-");
        var file = dir.At("t.jsonl");
        Write(file, Joined(Stamped(At(10, 0))));
        File.SetLastWriteTimeUtc(file, At(10, 0));

        TranscriptLines.WrittenSince(file, At(10, 1)).Should().BeFalse("it was last written before the bound, so it holds nothing later");
        TranscriptLines.WrittenSince(file, At(10, 0)).Should().BeTrue("at the bound is not before it");
        TranscriptLines.WrittenSince(file, At(9, 0)).Should().BeTrue();
    }

    [Fact]
    public void AgentLog_skips_a_transcript_last_written_before_its_window_opened()
    {
        // The proof that AgentLog.Files really goes through WrittenSince: the line is in the window, the file's mtime is not.
        using var dir = TempDir.For("testsToDelete-coai-transcript-agentlog-");
        var folder = Directory.CreateDirectory(dir.At("d--rsd-repo")).FullName;
        var file = Path.Combine(folder, "session.jsonl");
        var line = "{\"timestamp\":\"" + Stamp(At(13, 20)) + "\",\"type\":\"assistant\",\"cwd\":\"D:/repo\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"inside\"}]}}";
        Write(file, Joined(line));

        File.SetLastWriteTimeUtc(file, At(13, 30));
        AgentLog.Slice(dir, At(13, 0), At(13, 39), "D:/repo").Should().Contain("inside", "a file written inside the window is read");

        File.SetLastWriteTimeUtc(file, At(12, 0));
        AgentLog.Slice(dir, At(13, 0), At(13, 39), "D:/repo").Should().BeEmpty("a file last written before the window opened is not read at all");
    }

    // ---- the byte gate: ReadSharedFrom / StampAtOrAfter ------------------------------------------------------------------

    [Fact]
    public void A_line_stamped_before_the_bound_is_skipped()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-skip-");
        var file = dir.At("t.jsonl");
        string[] lines =
        [
            Stamped(At(11, 0)), Stamped(At(9, 0)), Stamped(At(9, 1)), Stamped(At(9, 2)), Stamped(At(11, 5)),
        ];
        Write(file, Joined(lines));
        var meter = new ScanMeter();

        var yielded = Read(file, At(10, 30), meter);

        yielded.Should().Equal([lines[0], lines[4]], "the head line and the one after the bound; the three stale ones are not yielded");
        meter.LinesSkipped.Should().Be(3);
        meter.BytesYielded.Should().Be(Bytes(lines[0]) + 1 + Bytes(lines[4]) + 1);
        meter.BytesScanned.Should().Be(new FileInfo(file).Length, "every byte read counts as scanned, yielded or not");
    }

    [Fact]
    public void A_line_without_any_stamp_is_yielded()
    {
        // The top-level stamp comes after `message`, so a line torn in the middle usually has no key at all: skipping it would hide
        // every torn MIDDLE line under a bound and `partial: torn line` could never fire.
        using var dir = TempDir.For("testsToDelete-coai-transcript-nostamp-");
        var file = dir.At("t.jsonl");
        var noKey = "{\"type\":\"last-prompt\",\"lastPrompt\":\"x\"}";
        var tornMiddle = "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"usage\":{\"input_tokens\":1";
        var stale = Stamped(At(9, 0));
        Write(file, Joined(Stamped(At(11, 0)), noKey, stale, tornMiddle, Stamped(At(11, 1))));
        var meter = new ScanMeter();

        var yielded = Read(file, At(10, 30), meter);

        yielded.Should().Contain([noKey, tornMiddle]);
        yielded.Should().NotContain(stale, "the gate still works on the lines that do carry a stamp");
        meter.LinesSkipped.Should().Be(1);
        TranscriptLines.TryParse(Utf8(tornMiddle), out _).Should().BeFalse("and the torn one reaches TryParse, which is what says torn");
    }

    [Theory]
    [InlineData(4 * 1024)]
    [InlineData(200 * 1024)]
    public void A_line_longer_than_the_cap_is_skipped_not_buffered(int tooLong)
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-cap-");
        var file = dir.At("t.jsonl");
        string[] shorts = [Stamped(At(10, 0)), Stamped(At(10, 1))];
        var huge = Stamped(At(10, 2), new string('x', tooLong));
        Write(file, Joined(shorts[0], huge, shorts[1]));
        var meter = new ScanMeter { MaxLineBytes = 1024 };

        var yielded = Read(file, DateTime.MinValue, meter);

        yielded.Should().Equal(shorts, "the long line is not in the slice and the two short ones either side are");
        meter.LinesTooLong.Should().Be(1);
        meter.BytesScanned.Should().Be(new FileInfo(file).Length, "the bytes of a skipped line are still scanned");
        meter.BytesYielded.Should().Be(Bytes(shorts[0]) + 1 + Bytes(shorts[1]) + 1);
    }

    [Fact]
    public void A_too_long_last_line_with_no_terminator_is_counted_and_not_yielded()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-captail-");
        var file = dir.At("t.jsonl");
        var first = Stamped(At(10, 0));
        Write(file, first + "\n" + Stamped(At(10, 1), new string('x', 200 * 1024)));
        var meter = new ScanMeter { MaxLineBytes = 1024 };

        Read(file, DateTime.MinValue, meter).Should().Equal(first);
        meter.LinesTooLong.Should().Be(1);
    }

    [Fact]
    public void The_stop_hook_is_asked_before_a_line_is_yielded()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-stopfirst-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1)), Stamped(At(10, 2))];
        Write(file, Joined(lines));
        var first = Bytes(lines[0]) + 1;
        var meter = new ScanMeter();
        meter.Stop = () => meter.BytesYielded > first;

        var yielded = Read(file, DateTime.MinValue, meter);

        yielded.Should().Equal(lines[0]);
        meter.BytesYielded.Should().Be(first + Bytes(lines[1]) + 1, "the second line is charged, so the slice never holds a line past the budget, and it is not yielded");
    }

    [Fact]
    public void The_stop_hook_ends_the_read()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-stop-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1)), Stamped(At(10, 2))];
        Write(file, Joined(lines));
        var meter = new ScanMeter();
        var seen = 0;
        meter.Stop = () => seen >= 1;

        var yielded = new List<string>();
        foreach (var line in TranscriptLines.ReadSharedFrom(file, DateTime.MinValue, meter))
        {
            yielded.Add(Encoding.UTF8.GetString(line.Span));
            seen++;
        }

        yielded.Should().Equal([lines[0]], "after the first line the hook says stop, and the sequence ends");
    }

    [Fact]
    public void The_stop_hook_is_asked_while_lines_are_being_skipped_too()
    {
        // A file of stale lines yields nothing, so a stop that is asked only per yielded line would let the clock run unchecked.
        using var dir = TempDir.For("testsToDelete-coai-transcript-stopscan-");
        var file = dir.At("t.jsonl");
        var stale = Stamped(At(9, 0), new string('y', 900));
        Write(file, Joined([Stamped(At(11, 0)), .. Enumerable.Repeat(stale, 3 * 1024)]));
        var meter = new ScanMeter();
        meter.Stop = () => meter.BytesScanned >= 1024 * 1024;

        Read(file, At(10, 30), meter).Should().HaveCount(1);

        meter.BytesScanned.Should().BeLessThan(new FileInfo(file).Length, "the read ended at the first MiB, not at the end of the file");
        meter.BytesScanned.Should().BeGreaterThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public void Any_stamp_at_or_after_the_bound_keeps_the_line()
    {
        // The top-level stamp comes AFTER `message`, so the first occurrence can be a key in a nested object and the last can be one
        // inside a tool result. With "any occurrence" a nested key can cost one extra parse; it can never skip the line's own stamp.
        var bound = At(10, 30);
        var before = Stamp(At(9, 0));
        var after = Stamp(At(11, 0));
        string Line(string nested, string top) =>
            "{\"message\":{\"timestamp\":\"" + nested + "\"},\"timestamp\":\"" + top + "\"}";

        TranscriptLines.StampAtOrAfter(Utf8(Line(after, before)), bound).Should().BeTrue("a nested key at or after the bound keeps the line");
        TranscriptLines.StampAtOrAfter(Utf8(Line(before, after)), bound).Should().BeTrue("the line's own stamp is the LAST occurrence here");
        TranscriptLines.StampAtOrAfter(Utf8(Line(before, before)), bound).Should().BeFalse("only when every occurrence is before the bound");
    }

    [Fact]
    public void A_stamp_written_with_a_space_is_still_seen()
    {
        var bound = At(10, 30);

        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\": \"" + Stamp(At(11, 0)) + "\"}"), bound).Should().BeTrue();
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\": \"" + Stamp(At(9, 0)) + "\"}"), bound).Should().BeFalse();
    }

    [Fact]
    public void A_stamp_that_is_not_the_expected_form_is_treated_as_at_or_after()
    {
        var bound = At(10, 30);

        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":null}"), bound).Should().BeTrue("not a stamp: the JSON decides");
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"2000-01-01T09"), bound).Should().BeTrue("a stamp cut short");
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"yesterday afternoon!!\"}"), bound).Should().BeTrue();
        TranscriptLines.StampAtOrAfter(Utf8("{\"type\":\"ai-title\"}"), bound).Should().BeTrue("no key at all: the line is parsed and the JSON decides");
    }

    [Fact]
    public void The_bound_is_floored_to_the_second()
    {
        var bound = At(10, 30, 5).AddMilliseconds(900);

        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"" + Stamp(At(10, 30, 5)) + "\"}"), bound).Should().BeTrue("10:30:05.000 is in the same second as the bound");
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"" + Stamp(At(10, 30, 4)) + "\"}"), bound).Should().BeFalse();
    }

    [Fact]
    public void The_head_is_yielded_up_to_the_first_stamped_line()
    {
        // A file can start with an `ai-title`, which has no timestamp (1 of 21 Windows main files).
        using var dir = TempDir.For("testsToDelete-coai-transcript-head-");
        var file = dir.At("t.jsonl");
        string[] lines = ["{\"type\":\"ai-title\",\"aiTitle\":\"x\"}", Stamped(At(9, 0)), Stamped(At(9, 1))];
        Write(file, Joined(lines));
        var meter = new ScanMeter();

        Read(file, At(10, 30), meter).Should().Equal(lines[0], lines[1]);
        meter.LinesSkipped.Should().Be(1);
    }

    [Fact]
    public void An_unterminated_last_line_is_always_yielded()
    {
        // A torn tail usually ends before its stamp; the byte gate must still hand it over.
        using var dir = TempDir.For("testsToDelete-coai-transcript-tail-");
        var file = dir.At("t.jsonl");
        var head = Stamped(At(11, 0));
        var tail = "{\"type\":\"assistant\",\"message\":{\"id\":\"m9\"";
        Write(file, head + "\n" + Stamped(At(9, 0)) + "\n" + tail);
        var meter = new ScanMeter();

        Read(file, At(10, 30), meter).Should().Equal(head, tail);
        meter.LinesSkipped.Should().Be(1);
        meter.BytesYielded.Should().Be(Bytes(head) + 1 + Bytes(tail), "no terminator was read, so none is charged");

        var staleTail = Stamped(At(9, 5));
        Write(file, head + "\n" + staleTail);
        Read(file, At(10, 30), new ScanMeter()).Should().Equal(head, staleTail);
    }

    [Fact]
    public void Lines_out_of_order_are_judged_one_by_one()
    {
        // Stamps are not always in file order (11 of 19 fixtures), so no "first line past the bound ends the file".
        using var dir = TempDir.For("testsToDelete-coai-transcript-order-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(11, 0)), Stamped(At(10, 0)), Stamped(At(12, 0)), Stamped(At(9, 0)), Stamped(At(10, 45))];
        Write(file, Joined(lines));
        var meter = new ScanMeter();

        Read(file, At(10, 30), meter).Should().Equal(lines[0], lines[2], lines[4]);
        meter.LinesSkipped.Should().Be(2);
    }

    [Fact]
    public void Bytes_in_the_slice_are_bytes_not_chars()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-bytes-");
        var file = dir.At("t.jsonl");
        var line = Stamped(At(10, 0), "\u20ac");
        Write(file, Joined(line));
        var meter = new ScanMeter();

        Read(file, DateTime.MinValue, meter).Should().Equal(line);

        line.Length.Should().BeLessThan(Bytes(line), "the euro sign is one char and three bytes");
        meter.BytesYielded.Should().Be(Bytes(line) + 1);
    }

    [Fact]
    public void A_line_longer_than_a_read_block_is_one_line()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-block-");
        var file = dir.At("t.jsonl");
        var big = Stamped(At(10, 0), new string('z', 200 * 1024));
        var small = Stamped(At(10, 1));
        Write(file, Joined(big, small));

        // Compared by length: a failure message that printed 200 KiB of padding would bury the symptom.
        Read(file, DateTime.MinValue, new ScanMeter()).Select(line => line.Length).Should().Equal(big.Length, small.Length);
    }

    [Fact]
    public void A_CRLF_ends_a_line_and_is_not_yielded()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-crlf-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1))];
        Write(file, string.Join("\r\n", lines) + "\r\n");
        var meter = new ScanMeter();

        var yielded = Read(file, DateTime.MinValue, meter);

        yielded.Should().Equal(lines);
        yielded.Should().OnlyContain(line => !line.Contains('\r'));
        meter.BytesScanned.Should().Be(new FileInfo(file).Length);
    }

    [Fact]
    public void A_blank_line_is_not_a_line()
    {
        using var dir = TempDir.For("testsToDelete-coai-transcript-blank-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1))];
        Write(file, lines[0] + "\n\n" + lines[1] + "\n");
        var meter = new ScanMeter();

        Read(file, DateTime.MinValue, meter).Should().Equal(lines, "an empty line is not a torn one, and it would only be counted as one");
        meter.LinesSkipped.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_read_that_fails_after_the_open_ends_that_file_and_is_counted(bool unauthorized)
    {
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1))];
        var first = Utf8(Joined(lines) + "{\"half\":");
        using var stream = new FailingStream(first, unauthorized ? new UnauthorizedAccessException() : new IOException());
        var meter = new ScanMeter();

        var yielded = TranscriptLines.ReadFrom(stream, DateTime.MinValue, meter)
            .Select(line => Encoding.UTF8.GetString(line.Span)).ToList();

        yielded.Should().Equal(lines, "the whole lines before the failure are kept; the unterminated remainder is not a tail any more");
        meter.ReadFailures.Should().Be(1);
    }

    [Fact]
    public void The_gate_never_drops_a_line_a_full_parse_places_at_or_after_the_bound()
    {
        // The positive anchor over E1's fixtures (read only, no copy): the byte gate must keep EVERY line a JsonDocument parse
        // would place at or after the bound — including the 11 cases whose stamps are not in file order.
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "caller-transcripts");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var dirs = manifest.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("hasTranscript").GetBoolean())
            .Select(c => c.GetProperty("dir").GetString()!).ToList();
        dirs.Should().NotBeEmpty("a scan that finds no fixture would pass for ever");

        var files = dirs.SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d, "projects"), "*.jsonl", SearchOption.AllDirectories)).ToList();
        files.Should().NotBeEmpty();

        var skippedSomewhere = 0;
        foreach (var file in files)
        {
            skippedSomewhere += AssertEveryLineAtOrAfterTheMedianIsYielded(file);
        }

        skippedSomewhere.Should().BeGreaterThan(0, "the gate must actually bite on some fixture, or the anchor proves nothing");
    }

    /// <summary>Returns the number of lines the gate skipped in this file.</summary>
    private static int AssertEveryLineAtOrAfterTheMedianIsYielded(string file)
    {
        var lines = File.ReadAllLines(file);
        var stamped = lines.Select(line => (Line: line, At: TopLevelStamp(line))).Where(x => x.At is not null).ToList();
        var bound = stamped.Select(x => x.At!.Value).Order().ElementAt(stamped.Count / 2);
        var meter = new ScanMeter();

        var yielded = Read(file, bound, meter).ToHashSet();

        stamped.Where(x => x.At >= bound).Select(x => x.Line).Should().OnlyContain(line => yielded.Contains(line), file);
        return meter.LinesSkipped;
    }

    private static DateTime? TopLevelStamp(string line)
    {
        if (!TranscriptLines.TryParse(line, out var document))
        {
            return null;
        }

        using (document)
        {
            return document.RootElement.TryGetProperty("timestamp", out var stamp) && stamp.ValueKind == JsonValueKind.String
                ? DateTime.Parse(stamp.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                : null;
        }
    }

    [Fact]
    public void A_bom_at_the_file_start_is_not_part_of_the_first_line()
    {
        // StreamReader (ReadShared) skips a UTF-8 BOM; JsonDocument.Parse over bytes does not, and would call line 1 torn. The two
        // readers must not drift, so the byte reader skips it too — at stream offset 0 only, and the bytes still count as scanned.
        using var dir = TempDir.For("testsToDelete-coai-transcript-bom-");
        var file = dir.At("t.jsonl");
        string[] lines = [Stamped(At(10, 0)), Stamped(At(10, 1))];
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. Utf8(Joined(lines))]);
        var meter = new ScanMeter();

        var viaStrings = TranscriptLines.ReadShared(file).ToList();
        var viaBytes = Read(file, DateTime.MinValue, meter);

        viaStrings.Should().Equal(lines, "the string reader has always skipped the BOM");
        viaBytes.Should().Equal(lines, "and the byte reader reads the same lines");
        TranscriptLines.TryParse(Utf8(viaBytes[0]), out var first).Should().BeTrue("the first line parses, so it is not reported torn");
        first?.Dispose();
        meter.BytesScanned.Should().Be(new FileInfo(file).Length, "the BOM bytes were read, so they are scanned");
        meter.BytesYielded.Should().Be(Bytes(lines[0]) + 1 + Bytes(lines[1]) + 1, "and they are not in the slice");
    }

    [Theory]
    [InlineData("2000-01-01T05:00:00-05:00")]
    [InlineData("2000-01-01T05:00:00+02:00")]
    [InlineData("2000-01-01T05:00:00")]
    public void A_stamp_without_a_Z_or_fraction_is_not_the_expected_form_so_the_line_is_kept(string stamp)
    {
        // 05:00 at -05:00 is 10:00 UTC. Compared as text it would read as 05:00 and be skipped under a 10:30 bound.
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"" + stamp + "\"}"), At(10, 30)).Should().BeTrue("the JSON decides, not the text");
    }

    [Fact]
    public void A_stamp_in_the_expected_form_with_no_fraction_is_still_compared()
    {
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"2000-01-01T09:00:00Z\"}"), At(10, 30)).Should().BeFalse();
        TranscriptLines.StampAtOrAfter(Utf8("{\"timestamp\":\"2000-01-01T11:00:00Z\"}"), At(10, 30)).Should().BeTrue();
    }

    /// <summary>Delivers its bytes in one read, then fails the next one the way a vanishing share does.</summary>
    private sealed class FailingStream(byte[] first, Exception failure) : Stream
    {
        private bool _delivered;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_delivered)
            {
                throw failure;
            }

            _delivered = true;
            first.CopyTo(buffer, offset);
            return first.Length;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_delivered)
            {
                throw failure;
            }

            _delivered = true;
            first.CopyTo(buffer);
            return first.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
