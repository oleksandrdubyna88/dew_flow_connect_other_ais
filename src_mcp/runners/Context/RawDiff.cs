namespace CoaiMcp.Runners.Context;

/// <summary>
/// Reading <c>git diff --raw -z</c> for the one fact a numstat cannot give: how many submodule pointers a range changed.
/// </summary>
/// <remarks>
/// <para>The parent repository's numstat shows a submodule change as ONE gitlink line (<c>1 1 libs/sub</c>), so a size
/// built from numstats cannot cover the submodule's own edits — and cannot even tell a pointer from a file. The raw form
/// can: each record is <c>:oldmode newmode oldsha newsha status</c> followed by one NUL-terminated path (two for a rename or
/// a copy), and a gitlink is mode <c>160000</c> on either side. Pure: a string in, a count out; no path is kept.</para>
/// </remarks>
public static class RawDiff
{
    private const string GitlinkMode = "160000";
    private const int Fields = 5;

    /// <summary>One raw record's meta line, read: whether it touches a gitlink, and how many path tokens follow it.</summary>
    private sealed record Entry(bool Gitlink, int Paths)
    {
        public int Count => Gitlink ? 1 : 0;
    }

    /// <summary>
    /// The number of entries whose old or new mode is a gitlink — 0 for an empty diff, which is a measurement — or null when
    /// the text is not a raw diff, because a count taken from a stream that does not parse would be a guess.
    /// </summary>
    public static int? GitlinkCount(string raw)
    {
        var tokens = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var gitlinks = 0;
        for (var at = 0; at < tokens.Length;)
        {
            // The entry, and every path token it requires, must be there: a stream cut before a path is no stream.
            if (Read(tokens[at]) is not { } entry || at + entry.Paths >= tokens.Length)
            {
                return null;
            }

            gitlinks += entry.Count;
            at += 1 + entry.Paths;
        }

        return gitlinks;
    }

    private static Entry? Read(string meta)
    {
        var fields = meta.Split(' ');

        return meta.StartsWith(':') && fields.Length == Fields && PathsOf(fields[Fields - 1]) is > 0 and var paths
            ? new Entry(IsGitlink(fields), paths)
            : null;
    }

    private static bool IsGitlink(string[] fields) => fields[0][1..] == GitlinkMode || fields[1] == GitlinkMode;

    /// <summary>
    /// A rename or a copy names two paths; every other status names one. A status git does not print — a letter outside
    /// <c>ACDMRTUX</c>, or anything but digits after it (the similarity score) — is no entry at all (0 paths = refused).
    /// </summary>
    private static int PathsOf(string status) =>
        status is [var letter, .. var score] && IsStatus(letter, score)
            ? (letter is 'R' or 'C' ? 2 : 1)
            : 0;

    private static bool IsStatus(char letter, ReadOnlySpan<char> score) => Letters.Contains(letter) && !score.ContainsAnyExceptInRange('0', '9');

    private const string Letters = "ACDMRTUX";
}
