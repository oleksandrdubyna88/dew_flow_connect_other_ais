namespace CoaiMcp.Core.Context;

/// <summary>
/// One `git diff` output, split back into the per-file pieces the shaper budgets whole.
/// </summary>
/// <remarks>
/// <para>Why this exists: asking git once for every file and splitting is one process, while asking
/// per file is one process EACH — 30-50 ms apiece on Windows, so a fifty-file change paid seconds
/// before a consultant was launched. The shaper still elides a file ENTIRE, so the pieces have to be
/// recovered rather than budgeted as one blob.</para>
/// <para>The boundary is git's own <c>diff --git a/x b/x</c> header. The PATH is taken from the
/// <c>+++ b/…</c> line, and only from it: the header line cannot be parsed for a path that contains a
/// space (`a/my file b/my file` is ambiguous by construction), while `+++` is one path to end of
/// line. A deletion has `+++ /dev/null` and falls back to the `--- a/…` side.</para>
/// </remarks>
public static class DiffSplitter
{
    private const string Boundary = "diff --git ";

    public static IReadOnlyDictionary<string, string> ByFile(string diff)
    {
        var pieces = new Dictionary<string, string>(StringComparer.Ordinal);
        if (diff.Length == 0)
        {
            return pieces;
        }

        var lines = diff.Split('\n');
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(Boundary, StringComparison.Ordinal))
            {
                continue;
            }

            Keep(pieces, lines, start, i);
            start = i;
        }

        Keep(pieces, lines, start, lines.Length);

        return pieces;
    }

    private static void Keep(Dictionary<string, string> pieces, string[] lines, int start, int end)
    {
        if (start < 0)
        {
            return;
        }

        var span = lines[start..end];
        if (PathIn(span) is { Length: > 0 } path)
        {
            // A rename produces one hunk under the NEW name, which is the name the numstat row
            // carries as well, so the two agree without either knowing about the other.
            pieces[path] = string.Join('\n', span).TrimEnd('\n') + "\n";
        }
    }

    private static string PathIn(string[] lines)
    {
        foreach (var line in lines)
        {
            var marker = line.TrimEnd('\r');
            if (marker.StartsWith("+++ ", StringComparison.Ordinal) && Side(marker[4..], 'b') is { Length: > 0 } added)
            {
                return added;
            }

            if (marker.StartsWith("--- ", StringComparison.Ordinal) && HasNoNewSide(lines)
                && Side(marker[4..], 'a') is { Length: > 0 } removed)
            {
                return removed;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The path after a <c>+++</c>/<c>---</c> marker: <c>b/x</c>, or git's QUOTED <c>"b/x"</c> form.
    /// </summary>
    /// <remarks>
    /// Git prints a path it cannot render plainly — non-ASCII, a tab, a quote, a newline — inside
    /// double quotes with C-style escapes. The numstat side is read with <c>-z</c>, which never
    /// quotes, so the two spellings of one path did not match: the splitter recorded nothing under
    /// the quoted name and the assembler handed the consultant an EMPTY diff for that file, silently,
    /// with the file still listed. Every repository with a non-ASCII filename in it. (CodeRabbit, on
    /// the pull request.)
    /// </remarks>
    private static string Side(string value, char side)
    {
        if (value.StartsWith($"{side}/", StringComparison.Ordinal))
        {
            return value[2..];
        }

        return value.Length > 3 && value[0] == QUOTE && value[1] == side && value[2] == '/' && value[^1] == QUOTE
            ? Unquote(value[3..^1])
            : string.Empty;
    }

    /// <summary>Git's <c>quote_c_style</c> spelling, decoded — octal escapes are BYTES of UTF-8.</summary>
    private static string Unquote(string body)
    {
        var bytes = new List<byte>(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\')
            {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes([body[i]]));
                continue;
            }

            i = Escaped(body, i + 1, bytes);
        }

        return System.Text.Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>One escape, appended — and where the caller should carry on reading.</summary>
    private static int Escaped(string body, int at, List<byte> bytes)
    {
        if (at >= body.Length)
        {
            return at;
        }

        var known = EscapeNames.IndexOf(body[at]);
        if (known >= 0)
        {
            bytes.Add(EscapeBytes[known]);

            return at;
        }

        // Three octal digits are one BYTE: a non-ASCII name arrives as several of them in a row and
        // only becomes a character once they are all decoded together.
        if (body[at] is >= '0' and <= '7' && at + 2 < body.Length)
        {
            bytes.Add(Convert.ToByte(body.Substring(at, 3), 8));

            return at + 2;
        }

        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes([body[at]]));

        return at;
    }

    private const char QUOTE = '\"';

    private static readonly string EscapeNames = "abtnvfr\"\\";

    private static readonly byte[] EscapeBytes = [7, 8, 9, 10, 11, 12, 13, (byte)'\"', (byte)'\\'];

    private static bool HasNoNewSide(string[] lines) =>
        Array.Exists(lines, l => l.StartsWith("+++ /dev/null", StringComparison.Ordinal));
}
