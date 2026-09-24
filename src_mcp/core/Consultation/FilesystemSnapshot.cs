using System.Collections.Immutable;
using System.Text;

namespace CoaiMcp.Core.Consultation;

/// <summary>What one path in the tree looked like at one moment, as a comparable string.</summary>
/// <param name="Kind"><c>tracked</c>, <c>untracked</c>, <c>ignored</c> or <c>git</c> — decides the verb a change gets.</param>
/// <param name="Fingerprint">The git status code plus size and modification time — whatever this
/// kind of path can be asked about. Two equal fingerprints mean "nothing observable changed".</param>
public sealed record TreeEntry(string Kind, string Fingerprint);

/// <summary>One path that changed under the consultant, and what happened to it.</summary>
public sealed record TreeChange(string Path, string What);

/// <summary>
/// The working tree as a set of fingerprints — the pure half of the filesystem invariant.
/// </summary>
/// <remarks>
/// <para>Taken before a consultant is launched and again after it exits. The comparison is a pure
/// function so every verb it can produce is a table test; COLLECTING the entries (git, stat) is the
/// runners' job.</para>
/// <para>The verbs name what a person has to do about it, and nothing here does anything about it:
/// after two Blocking findings on the plan round, this feature never deletes and never reverts. A
/// file that appeared during a multi-minute consultation may be the PERSON's.</para>
/// </remarks>
public sealed record FilesystemSnapshot(ImmutableSortedDictionary<string, TreeEntry> Entries)
{
    public static readonly FilesystemSnapshot Empty = new(
        ImmutableSortedDictionary<string, TreeEntry>.Empty.WithComparers(StringComparer.Ordinal));

    public static IReadOnlyList<TreeChange> Compare(FilesystemSnapshot before, FilesystemSnapshot after)
    {
        var changes = new List<TreeChange>();
        foreach (var (path, entry) in after.Entries)
        {
            if (!before.Entries.TryGetValue(path, out var was))
            {
                changes.Add(new TreeChange(path, Shared(path, Appeared(entry.Kind))));
            }
            else if (was.Fingerprint != entry.Fingerprint)
            {
                changes.Add(new TreeChange(path, Shared(path, Changed(entry.Kind))));
            }
        }

        changes.AddRange(before.Entries.Keys.Except(after.Entries.Keys)
            .Select(path => new TreeChange(path, Shared(path, Vanished(before.Entries[path].Kind)))));

        return changes;
    }

    /// <summary>The alert, one sentence, every path with its verb.</summary>
    /// <remarks>
    /// It says the tree CHANGED, not that the consultant changed it. Two snapshots cannot name a
    /// writer: the person's own editor, a build watcher or a git command of their own can move a file
    /// in the same window, and the consultation is withheld either way because the advice was formed
    /// against a tree that no longer holds. Claiming the consultant did it would send somebody hunting
    /// a vendor for their own keystrokes. (The consultant's own second point, on this feature's live
    /// check.)
    /// </remarks>
    public static string Sentence(IReadOnlyList<TreeChange> changes)
    {
        var text = new StringBuilder("the working tree changed while the consultant was running, so its advice was withheld and nothing was touched (these snapshots cannot say who wrote): ");
        text.AppendJoin("; ", changes.Select(c => $"{c.Path} ({c.What})"));
        text.Append(". A deleted tracked file comes back with `git checkout -- <path>`; an added file is yours to inspect and remove.");

        return text.ToString();
    }

    /// <summary>
    /// A change to the COMMON git directory of a linked worktree, said to be shared — issue #376.
    /// </summary>
    /// <remarks>
    /// Every worktree of the repository writes that directory, and so does an editor open on any of them;
    /// the person reading a withheld consultation should not be led to blame the consultant for a sibling's
    /// <c>push -u</c>. The snapshots still cannot say who wrote, and the sentence does not pretend to.
    /// </remarks>
    private static string Shared(string path, string what) =>
        path.StartsWith("<git>/common/", StringComparison.Ordinal)
            ? $"shared {what} — every worktree of this repository can write it: a push -u, a branch switch or the editor in another checkout"
            : what;

    private static string Appeared(string kind) => kind switch
    {
        "tracked" => "tracked file changed",
        "git" => ".git metadata appeared",
        _ => "added",
    };

    private static string Changed(string kind) => kind switch
    {
        "tracked" => "modified",
        "ignored" => "ignored file changed",
        "git" => ".git metadata changed",
        _ => "untracked file changed",
    };

    private static string Vanished(string kind) => kind switch
    {
        "tracked" => "tracked change reverted or file deleted",
        "git" => ".git metadata removed",
        _ => "deleted",
    };
}
