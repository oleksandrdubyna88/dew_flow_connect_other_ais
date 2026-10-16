using System.Globalization;
using CoaiMcp.Core.Collecting;

namespace CoaiMcp.Core.Rounds;

public abstract record EntryVerdict
{
    private protected EntryVerdict()
    {
    }

    public sealed record Accepted(TreeEntry Entry) : EntryVerdict;

    /// <summary>The sentence names the path and the short commit, and the cure.</summary>
    public sealed record Refused(string Sentence) : EntryVerdict;
}

/// <summary>
/// What a PLAN requires of the one tree entry git listed for its path — decided on the entry alone, so no blob is
/// fetched for a path that can never be a plan.
/// </summary>
/// <remarks>
/// <para>Pure: <see cref="Pick"/> runs no process, and the reader asks for the blob only on <c>Accepted</c>. The rules
/// run in a fixed order and every refusal names the cure; the MODE decides before the type, because a committed
/// symlink's <c>ls-tree</c> type is <c>blob</c> (measured) and only mode <c>120000</c> says it is a link.</para>
/// <para>Several entries and a differing path are both reachable through a trailing slash: <c>todo/</c> lists what is
/// UNDER <c>todo</c>, so the one record's path is <c>todo/PLAN x.md</c> and not what was asked for.</para>
/// </remarks>
public static class PlanEntry
{
    private static readonly Dictionary<string, (string What, string Cure)> NotAFile = new()
    {
        ["040000"] = ("a directory", "pass the plan file inside it (todo/PLAN_x.md)"),
        ["120000"] = ("a symbolic link", "pass the path of the file it points to"),
        ["160000"] = ("a submodule", "a plan is a file of this repository"),
    };

    public static EntryVerdict Pick(TreeListing listing, string path, string sha)
    {
        var sha12 = GitIds.Short(sha);
        var shape = Shape(listing, path, sha12);

        return shape.Length > 0 ? new EntryVerdict.Refused(shape) : One(listing.Entries[0], path, sha12);
    }

    private static string Shape(TreeListing listing, string path, string sha12) =>
        listing.Malformed.Length > 0
            ? $"git ls-tree answered a record this reader cannot parse for '{path}' at commit {sha12} ('{listing.Malformed}') — not a state of the repository; re-run, and if it repeats report it with the git version"
            : listing.Entries.Count switch
            {
                0 => $"'{path}' is not in commit {sha12} — commit the plan on this branch first, or pass the path exactly as committed (case and separators count)",
                1 => string.Empty,
                var n => $"'{path}' names {N(n)} entries at commit {sha12} — pass the path of ONE committed file",
            };

    private static EntryVerdict One(TreeEntry entry, string path, string sha12) =>
        entry.Path != path
            ? new EntryVerdict.Refused($"git listed '{entry.Path}' for '{path}' at commit {sha12} — pass the path exactly as committed")
            : ByMode(entry, path, sha12);

    private static EntryVerdict ByMode(TreeEntry entry, string path, string sha12)
    {
        if (NotAFile.TryGetValue(entry.Mode, out var special))
        {
            return new EntryVerdict.Refused($"'{path}' is {special.What} at commit {sha12} — {special.Cure}");
        }

        return entry.Mode is "100644" or "100755"
            ? Regular(entry, path, sha12)
            : new EntryVerdict.Refused($"'{path}' has mode {entry.Mode} at commit {sha12} — only a regular file (100644 or 100755) can be a plan");
    }

    private static EntryVerdict Regular(TreeEntry entry, string path, string sha12)
    {
        if (entry.Type != "blob")
        {
            return new EntryVerdict.Refused($"'{path}' is a {entry.Type}, not a blob, at commit {sha12} — report it; a regular file is always a blob");
        }

        if (entry.Size == 0)
        {
            return new EntryVerdict.Refused($"'{path}' is empty at commit {sha12} (0 bytes) — commit the plan's text first");
        }

        return entry.Size > DocumentRules.MaxBytes
            ? new EntryVerdict.Refused($"'{path}' is {N(entry.Size)} bytes at commit {sha12}, over the {N(DocumentRules.MaxBytes)}-byte ceiling — split it (an epic's stories document) and review the parts")
            : new EntryVerdict.Accepted(entry);
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

}
