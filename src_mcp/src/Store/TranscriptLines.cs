using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a transcript its CLI is still appending to — shared by <see cref="AgentLog"/> and the caller-transcript reader, so the
/// two cannot drift (E3.S1 of <c>PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Two reads.</b> <see cref="ReadShared"/> is <c>AgentLog</c>'s own and is exactly what it was: strings, every line.
/// <see cref="ReadSharedFrom"/> is the new reader's: raw UTF-8 bytes, and a line before the bound is skipped on its bytes —
/// never decoded, never parsed (E1 Q6: a gate that still decodes every line saves a third of a 30 s read at best).</para>
/// <para><b>What the byte gate does not do.</b> It never stops early on a stamp and never assumes an order: stamps are not
/// always in file order (11 of 19 fixtures), so each line is judged alone. It skips nothing it cannot prove is before the
/// bound — a line with no stamp key, a stamp not in the expected form, a head line and an unterminated tail are all yielded.</para>
/// </remarks>
internal static class TranscriptLines
{
    /// <summary><c>yyyy-MM-ddTHH:mm:ss</c>: the part of a stamp compared, ordinal. <c>d</c> is a digit.</summary>
    private const string StampForm = "dddd-dd-ddTdd:dd:dd";

    private static ReadOnlySpan<byte> StampKey => "\"timestamp\":"u8;

    /// <summary>
    /// The file's lines, or none at all.
    /// </summary>
    /// <remarks>
    /// The CLI is appending to its own transcript while this reads it, so the file is opened shared
    /// and a failure to read is silence rather than an exception — this is a nicety attached to a
    /// round, and it does not get to fail one.
    /// </remarks>
    public static IEnumerable<string> ReadShared(string file)
    {
        StreamReader reader;
        try
        {
            reader = new StreamReader(new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        using (reader)
        {
            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// The file's lines as raw UTF-8, every line before <paramref name="boundUtc"/> skipped undecoded, or none at all.
    /// </summary>
    /// <remarks>
    /// The yielded memory is the enumerator's own buffer, valid until the next <c>MoveNext</c>: a caller that parses it must
    /// dispose its <see cref="JsonDocument"/> before moving on (<see cref="TryParse(ReadOnlyMemory{byte}, out JsonDocument?)"/>
    /// keeps the memory for the document's lifetime). Opened exactly as <see cref="ReadShared"/> is.
    /// </remarks>
    public static IEnumerable<ReadOnlyMemory<byte>> ReadSharedFrom(string file, DateTime boundUtc, ScanMeter meter)
    {
        if (!TryOpen(file, out var stream))
        {
            yield break;
        }

        using (stream)
        {
            foreach (var line in ReadFrom(stream, boundUtc, meter))
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// The same read over a stream the caller owns — what <see cref="ReadSharedFrom"/> does after the open, and the seam a test
    /// uses to make a read fail mid-way, which a real file will not do on demand.
    /// </summary>
    internal static IEnumerable<ReadOnlyMemory<byte>> ReadFrom(Stream stream, DateTime boundUtc, ScanMeter meter)
    {
        var lines = new GatedLines(stream, BoundStamp(boundUtc), meter);
        while (lines.TryNext(out var line))
        {
            yield return line;
        }
    }

    /// <summary>
    /// A line as a document, or false for a torn one. The caller owns and disposes the document. Nothing is pre-read here.
    /// </summary>
    public static bool TryParse(string line, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null; // a half-written last line, which is the normal state of a live transcript
            return false;
        }
    }

    /// <summary>
    /// The same over the line's raw UTF-8 — no full-line <c>string</c> ever exists (plan gate G4). The document uses the memory
    /// for its whole life, so dispose it before the enumerator moves on.
    /// </summary>
    public static bool TryParse(ReadOnlyMemory<byte> line, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    /// <summary>
    /// The days the window touches, as the text an entry's timestamp starts with.
    /// </summary>
    /// <remarks>
    /// A cheap gate in front of an expensive one: a transcript is tens of megabytes and the window
    /// is usually one afternoon of it, so a line that does not even mention a day in range is
    /// skipped by a substring scan rather than parsed as JSON. Empty for a window spanning more days
    /// than are worth listing, and then every line is parsed as before.
    /// </remarks>
    internal static IReadOnlyList<string> Days(DateTime fromUtc, DateTime toUtc)
    {
        var span = toUtc.Date - fromUtc.Date;
        if (span.TotalDays > 7)
        {
            return [];
        }

        return [.. Enumerable.Range(0, (int)span.TotalDays + 1).Select(n => fromUtc.Date.AddDays(n).ToString("yyyy-MM-dd"))];
    }

    internal static bool Mentions(string line, IReadOnlyList<string> days)
    {
        return days.Count == 0 || days.Any(day => line.Contains(day, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the file could hold anything at or after <paramref name="fromUtc"/>.
    /// </summary>
    /// <remarks>
    /// A file last written before the bound cannot contain a line after it, and skipping those is what keeps a read from
    /// touching every megabyte the CLI has ever written. Needs no order of stamps, which is why it is the file-level skip.
    /// </remarks>
    public static bool WrittenSince(string file, DateTime fromUtc) => File.GetLastWriteTimeUtc(file) >= fromUtc;

    /// <summary>
    /// Whether the line is at or after the bound, judged on its raw bytes — or might be, which is the same answer here.
    /// </summary>
    /// <remarks>
    /// <para>Every occurrence of <c>"timestamp":</c> is tested, because the top-level stamp comes AFTER <c>message</c> (237 of
    /// 240 fixture lines): the first occurrence can be a key inside a nested object, and the last can be one inside a tool
    /// result. With "any occurrence at or after" a nested key can cost one extra parse; it can never cause the line's own stamp
    /// to be skipped.</para>
    /// <para>False only when the key occurs and EVERY occurrence is before the bound. No key (a line torn before its stamp,
    /// <c>last-prompt</c>, <c>ai-title</c>), or a value not in <c>yyyy-MM-ddTHH:mm:ss</c> form, answers true and the JSON decides:
    /// skipping a torn middle line would hide it, and <c>partial: torn line</c> could never fire. The bound is floored to the second.</para>
    /// </remarks>
    public static bool StampAtOrAfter(ReadOnlySpan<byte> line, DateTime boundUtc) => StampAtOrAfter(line, BoundStamp(boundUtc));

    /// <summary>The same test with the bound already formatted by <see cref="BoundStamp"/> — what the per-line gate calls.</summary>
    internal static bool StampAtOrAfter(ReadOnlySpan<byte> line, ReadOnlySpan<byte> boundStamp)
    {
        var rest = line;
        var anyBefore = false;
        while (NextStamp(ref rest, out var value))
        {
            if (!IsBefore(value, boundStamp))
            {
                return true;
            }

            anyBefore = true;
        }

        return !anyBefore;
    }

    /// <summary>Whether the line carries the stamp key at all — the head rule's test.</summary>
    internal static bool HasStampKey(ReadOnlySpan<byte> line) => line.IndexOf(StampKey) >= 0;

    internal static byte[] BoundStamp(DateTime boundUtc) =>
        Encoding.ASCII.GetBytes(boundUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

    /// <summary>The value after the next stamp key, with <paramref name="rest"/> moved past the key. False when there is none.</summary>
    private static bool NextStamp(ref ReadOnlySpan<byte> rest, out ReadOnlySpan<byte> value)
    {
        var at = rest.IndexOf(StampKey);
        if (at < 0)
        {
            value = default;
            return false;
        }

        rest = rest[(at + StampKey.Length)..];
        value = ValueStart(rest);
        return true;
    }

    /// <summary>Past optional spaces and one opening quote.</summary>
    private static ReadOnlySpan<byte> ValueStart(ReadOnlySpan<byte> after)
    {
        var value = after.TrimStart((byte)' ');

        return value.Length > 0 && value[0] == (byte)'"' ? value[1..] : value;
    }

    /// <summary>True only for a value in the stamp form that is ordinally before the bound.</summary>
    private static bool IsBefore(ReadOnlySpan<byte> value, ReadOnlySpan<byte> boundStamp) =>
        HasStampForm(value) && value[..StampForm.Length].SequenceCompareTo(boundStamp) < 0;

    /// <summary>
    /// <c>yyyy-MM-ddTHH:mm:ss</c> followed by a fraction or <c>Z</c>. A stamp with an offset (<c>-05:00</c>) is NOT in the form:
    /// its text is local time, and comparing it with a UTC bound would skip a line that is after the bound.
    /// </summary>
    private static bool HasStampForm(ReadOnlySpan<byte> value) =>
        value.Length > StampForm.Length && InStampForm(value[..StampForm.Length]) && value[StampForm.Length] is (byte)'.' or (byte)'Z';

    private static bool InStampForm(ReadOnlySpan<byte> value)
    {
        for (var i = 0; i < StampForm.Length; i++)
        {
            if (!MatchesForm(StampForm[i], value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesForm(char form, byte actual) => form == 'd' ? char.IsAsciiDigit((char)actual) : actual == form;

    private static bool TryOpen(string file, [NotNullWhen(true)] out FileStream? stream)
    {
        try
        {
            // Unbuffered: the splitter reads its own 64 KiB blocks, so a second buffer would only copy them.
            stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stream = null;
            return false;
        }
    }
}
