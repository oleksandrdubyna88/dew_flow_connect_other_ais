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
    private sealed record Entry(bool Gitlink, int Paths);

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
            if (Read(tokens[at]) is not { } entry)
            {
                return null;
            }

            gitlinks += entry.Gitlink ? 1 : 0;
            at += 1 + entry.Paths;
        }

        return gitlinks;
    }

    private static Entry? Read(string meta)
    {
        var fields = meta.Split(' ');

        return meta.StartsWith(':') && fields.Length == Fields
            ? new Entry(IsGitlink(fields), PathsOf(fields[Fields - 1]))
            : null;
    }

    private static bool IsGitlink(string[] fields) => fields[0][1..] == GitlinkMode || fields[1] == GitlinkMode;

    /// <summary>A rename or a copy names two paths; every other status names one.</summary>
    private static int PathsOf(string status) => status is ['R' or 'C', ..] ? 2 : 1;
}
