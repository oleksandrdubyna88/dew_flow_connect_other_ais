using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;

namespace CoaiMcp.Server;

/// <summary>
/// The ONE method every repository a one-shot place copies goes through — a work tree granted directly and a repository found beneath a
/// granted parent alike (D1 of <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c>): the link chain checked first, git's answer
/// asked again (it closes the window between the walk and the copy), then the snapshot of the LIVE tree as git lists it.
/// </summary>
/// <remarks>
/// A granted root reached through a LINK is refused (P1 of the S3 plan round): the root itself, or any folder above it up to its work
/// tree's top — a copy through one would be of whatever it points at. Inside the root, a link git lists is refused by the snapshot
/// itself. The links are checked BEFORE git runs in the folder (a found repository's folder is never entered through a link), and git's
/// check is repeated here for a found repository.
/// </remarks>
internal sealed class RepositoryCopier(ReviewSnapshots maker, IProcessLauncher launcher)
{
    /// <summary>
    /// A copy of one repository — or the refusal of a folder git has no work tree for: the copy lists through git (D6), and a plain folder
    /// has no listing to trust until a mode for one exists.
    /// </summary>
    /// <param name="linkRoot">
    /// The granted root the repository was found beneath — links are checked from the repository up to and including it (D5), and git must
    /// call the repository the TOP of a work tree — or empty for a work-tree root granted directly: the links are checked up to its work
    /// tree's top, and any folder git calls inside a work tree is copied as it is.
    /// </param>
    public async Task<SnapshotOutcome> CopyAsync(string source, string destination, string linkRoot, CancellationToken ct)
    {
        if (GrantedPaths.LinkAbove(source, linkRoot) is { Length: > 0 } link)
        {
            return new SnapshotOutcome.Refused(GrantedPaths.LinkSentence(link, linkRoot.Length == 0 ? source : linkRoot));
        }

        var facts = await RepositoryFacts.AskAsync(launcher, source, ct);

        return Confirmed(facts, linkRoot)
            ? await maker.MakeAsync(new SnapshotSource.Live(source), destination, ct)
            : new SnapshotOutcome.Refused(GrantedPaths.NotAWorkTree(source));
    }

    private static bool Confirmed(RepositoryFacts facts, string linkRoot) =>
        facts.InsideWorkTree && (linkRoot.Length == 0 || facts.IsTop);
}
