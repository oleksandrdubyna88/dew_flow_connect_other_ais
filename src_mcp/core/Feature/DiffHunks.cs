using System.Globalization;

namespace CoaiMcp.Core.Feature;

/// <summary>
/// Reads one file's piece of a unified diff — the head-side spans for the <c>*</c> marks, and the
/// placed lines the member hunks are built from. Pure; the diff comes from the caller.
/// </summary>
/// <remarks>
/// <para><b>Only lines after the first <c>@@</c> are hunk lines.</b> A file's piece opens with
/// <c>diff --git</c>, <c>index</c>, <c>---</c> and <c>+++</c> header lines, and an ADDED line whose text
/// begins <c>++ </c> is printed as <c>+++ </c> — so a header is recognised by where it stands, never by
/// how it starts.</para>
/// <para>A pure deletion, <c>+c,0</c>, has no head lines of its own: git names the line BEFORE the gap.
/// Its span is <c>c..c+1</c>, the two lines either side of it, so a member ending just above the gap and
/// one starting just below it are both marked — which is how the feature-pack trial drew it.</para>
/// </remarks>
public static class DiffHunks
{
    private const string HunkStart = "@@";

    /// <summary>The head-side span of every hunk in a <c>git diff -U0</c> piece.</summary>
    public static IReadOnlyList<LineSpan> ChangedSpans(string piece) =>
        [.. Lines(piece).Where(IsHunkHeader).Select(NewSide).Where(s => s.Start >= 0).Select(ToSpan)];

    /// <summary>Every line of a <c>git diff -U3</c> piece, placed at its line at head.</summary>
    /// <remarks>
    /// A context or added line advances the head position; a deleted line does not, so it sits at the
    /// head line that follows it — the line a reader would look at to find where it went.
    /// </remarks>
    public static IReadOnlyList<DiffLine> Placed(string piece)
    {
        var placed = new List<DiffLine>();
        var hunk = -1;
        var at = 0;
        foreach (var line in Lines(piece).SkipWhile(l => !IsHunkHeader(l)))
        {
            if (IsHunkHeader(line))
            {
                (hunk, at) = (hunk + 1, Math.Max(NewSide(line).Start, 0));
                continue;
            }

            at = Place(placed, hunk, at, line);
        }

        return placed;
    }

    /// <summary>Records one hunk line and answers the head position after it.</summary>
    private static int Place(List<DiffLine> placed, int hunk, int at, string line)
    {
        // An empty string is the tail after the piece's last newline: git prints an empty CONTEXT line
        // as a single space, so a line with no marker at all is never a hunk line.
        var kind = line.Length > 0 ? line[0] : '\0';
        if (kind is not (' ' or '+' or '-'))
        {
            return at; // "\ No newline at end of file", or a blank trailer: nothing at head
        }

        placed.Add(new DiffLine(hunk, kind, at, line));

        return kind == '-' ? at : at + 1;
    }

    private static IEnumerable<string> Lines(string piece) =>
        piece.Split('\n').Select(l => l.TrimEnd('\r'));

    private static bool IsHunkHeader(string line) => line.StartsWith(HunkStart, StringComparison.Ordinal);

    private static LineSpan ToSpan((int Start, int Count) side) =>
        side.Count == 0 ? new LineSpan(side.Start, side.Start + 1) : new LineSpan(side.Start, side.Start + side.Count - 1);

    /// <summary>The <c>+c[,d]</c> of a hunk header, as (start, count); count defaults to one, start is -1 when unreadable.</summary>
    private static (int Start, int Count) NewSide(string header)
    {
        var plus = header.IndexOf(" +", StringComparison.Ordinal);
        var end = plus < 0 ? -1 : header.IndexOf(' ', plus + 2);
        if (plus < 0 || end < 0)
        {
            return (-1, 0);
        }

        var parts = header[(plus + 2)..end].Split(',');

        return (Number(parts[0]), parts.Length > 1 ? Number(parts[1]) : 1);
    }

    private static int Number(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
