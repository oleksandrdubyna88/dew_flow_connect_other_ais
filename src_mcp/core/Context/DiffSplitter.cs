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
            if (line.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                return line[6..].TrimEnd('\r');
            }

            if (line.StartsWith("--- a/", StringComparison.Ordinal) && HasNoNewSide(lines))
            {
                return line[6..].TrimEnd('\r');
            }
        }

        return string.Empty;
    }

    private static bool HasNoNewSide(string[] lines) =>
        Array.Exists(lines, l => l.StartsWith("+++ /dev/null", StringComparison.Ordinal));
}
