namespace CoaiMcp.Core.Consultation;

/// <summary>
/// What a git config SAYS, for the consultation's filesystem invariant — issue #376.
/// </summary>
/// <remarks>
/// <para>The invariant used to compare a config's bytes. That withheld a consultation whenever anything
/// wrote ordinary bookkeeping into it while the consultant ran: a sibling worktree's <c>git push -u</c>
/// (<c>branch.X.remote</c> / <c>.merge</c>), VS Code's <c>branch.X.vscode-merge-base</c>, GitLens'
/// <c>branch.X.gk-*</c> — and when the checkout is a linked worktree, the config is SHARED with every one
/// of them.</para>
/// <para>So the config is compared by meaning: the entries <c>git config --list -z</c> prints, with that
/// bookkeeping dropped, sorted. Everything that can make the next git command run somebody else's code or
/// send a push somewhere else is kept — <c>core.*</c>, <c>alias.*</c>, <c>filter.*</c>,
/// <c>diff.*.textconv</c>, <c>credential.*</c>, <c>url.*</c>, <c>remote.*</c>, <c>include*</c>,
/// <c>submodule.*</c> — and so is <c>branch.X.description</c>, free text of any size that nothing gains
/// from leaving unwatched. <c>branch.X.remote</c> / <c>.pushremote</c> are dropped only when they say
/// <c>.</c> or name a remote whose <c>url</c> this same file defines: a URL there, or a name resolved from
/// another scope, is where the next push goes.</para>
/// </remarks>
public static class ConfigMeaning
{
    private static readonly string[] TrackingKeys = ["merge", "rebase", "vscode-merge-base"];

    private static readonly string[] WhereItPushes = ["remote", "pushremote"];

    /// <summary>The config's meaning as one comparable text, from <c>git config --list -z</c> output.</summary>
    public static string Of(string listed)
    {
        var entries = (listed ?? string.Empty)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(Entry.Parse)
            .ToList();
        var remotes = entries
            .Where(e => e.Section == "remote" && e.Variable == "url" && e.Subsection.Length > 0)
            .Select(e => e.Subsection)
            .ToHashSet(StringComparer.Ordinal);

        return string.Join('\0', entries
            .Where(e => !IsBookkeeping(e, remotes))
            .Select(e => e.Raw)
            .Order(StringComparer.Ordinal));
    }

    private static bool IsBookkeeping(Entry entry, IReadOnlySet<string> remotes) =>
        entry.Section == "branch" && entry.Subsection.Length > 0 && IsTracking(entry, remotes);

    private static bool IsTracking(Entry entry, IReadOnlySet<string> remotes) =>
        TrackingKeys.Contains(entry.Variable)
        || entry.Variable.StartsWith("gk-", StringComparison.Ordinal)
        || (WhereItPushes.Contains(entry.Variable) && (entry.Value == "." || remotes.Contains(entry.Value)));

    /// <summary>One <c>key\nvalue</c> entry of <c>--list -z</c>.</summary>
    /// <remarks>
    /// The VARIABLE is what follows the key's LAST dot and the subsection everything between the first and
    /// the last: branch names contain dots (<c>branch.rel.1.0.merge</c>). git prints section and variable in
    /// lower case and the subsection as written.
    /// </remarks>
    private sealed record Entry(string Raw, string Section, string Subsection, string Variable, string Value)
    {
        public static Entry Parse(string raw)
        {
            var newline = raw.IndexOf('\n');
            var key = newline < 0 ? raw : raw[..newline];
            var value = newline < 0 ? string.Empty : raw[(newline + 1)..];
            var first = key.IndexOf('.');
            var last = key.LastIndexOf('.');
            if (first < 0)
            {
                return new Entry(raw, key, string.Empty, string.Empty, value);
            }

            var subsection = last > first ? key[(first + 1)..last] : string.Empty;

            return new Entry(raw, key[..first], subsection, key[(last + 1)..], value);
        }
    }
}
