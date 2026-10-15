using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Runners.Collecting;

/// <summary>What reading a committed plan produced — the text and what it was, or one sentence naming the cure.</summary>
public abstract record PlanRead
{
    private protected PlanRead()
    {
    }

    /// <param name="Source">What was reviewed, as the session file records it.</param>
    /// <param name="Text">The proved text, exactly what the reviewers get.</param>
    /// <param name="Note">A line the caller reads beside the reviewers (the working tree was not compared), or empty.</param>
    public sealed record Accepted(PlanSource Source, string Text, string Note) : PlanRead;

    public sealed record Refused(string Sentence) : PlanRead;
}

/// <summary>
/// Reads one committed file as the plan to review, and proves the text handed on IS the blob git names.
/// </summary>
/// <remarks>
/// <para><b>Accepted if and only if the text re-encodes to the recorded blob id.</b> The sequence: the commit is a
/// SHA-1 id; <c>ls-tree</c> says what the path is (<see cref="PlanEntry"/> decides, with no blob fetched for a path that
/// can never be a plan); <c>cat-file</c> fetches the blob; the capture is refused when the launcher cut it, then proved
/// against the id (<see cref="BlobProof"/>); only the PROVED text is tested for emptiness and for being an LFS pointer.
/// Each read is refused before its words are trusted: a cut capture of a command that failed is still a failed command.</para>
/// <para>The path arrives already canonical and the guards around the checkout (credential shapes, a clean working
/// copy) are the caller's; this reads the OBJECT and never the working tree.</para>
/// </remarks>
public static class CommittedPlanReader
{
    public static async Task<PlanRead> ReadAsync(
        GitHistory git, string repoPath, string sha, string canonicalPath, CancellationToken ct)
    {
        if (!GitHistory.IsCommitish(sha))
        {
            return new PlanRead.Refused(NotSha1(sha));
        }

        var listing = await git.EntryAtAsync(repoPath, sha, canonicalPath, ct);
        var gate = CaptureGate(listing, $"listing '{canonicalPath}' at commit {Short(sha)}");

        return gate.Length > 0
            ? new PlanRead.Refused(gate)
            : await Entered(git, repoPath, sha, canonicalPath, TreeEntries.Parse(listing.Out), ct);
    }

    private static async Task<PlanRead> Entered(
        GitHistory git, string repoPath, string sha, string path, TreeListing listing, CancellationToken ct)
    {
        var verdict = PlanEntry.Pick(listing, path, sha);

        return verdict is EntryVerdict.Accepted accepted
            ? await ReadBlobAsync(git, repoPath, sha, path, accepted.Entry, ct)
            : new PlanRead.Refused(((EntryVerdict.Refused)verdict).Sentence);
    }

    private static async Task<PlanRead> ReadBlobAsync(
        GitHistory git, string repoPath, string sha, string path, TreeEntry entry, CancellationToken ct)
    {
        var answer = await git.BlobAsync(repoPath, entry.Oid, ct);
        var gate = CaptureGate(answer, $"reading blob {Short(entry.Oid)} of '{path}'");

        return gate.Length > 0 ? new PlanRead.Refused(gate) : Prove(answer.Out, sha, path, entry);
    }

    private static PlanRead Prove(string captured, string sha, string path, TreeEntry entry) =>
        BlobProof.Prove(captured, entry.Oid, entry.Size) switch
        {
            ProofVerdict.Proved proved => Body(proved.Text, sha, path, entry),
            ProofVerdict.Refused refused => new PlanRead.Refused($"'{path}' at commit {Short(sha)}: {refused.Sentence}"),
            _ => throw new InvalidOperationException("a proof verdict is Proved or Refused"),
        };

    /// <summary>Empty and LFS, decided on the text HANDED ON — never on a capture nothing has proved.</summary>
    private static PlanRead Body(string text, string sha, string path, TreeEntry entry)
    {
        if (text.All(c => char.IsWhiteSpace(c) || c == '\uFEFF'))
        {
            return new PlanRead.Refused($"'{path}' at commit {Short(sha)} holds only whitespace — commit the plan's text first");
        }

        return LfsPointer.IsPointer(text)
            ? new PlanRead.Refused($"'{path}' at commit {Short(sha)} is a git-lfs pointer, not the plan — commit the plan's text itself (git lfs untrack), or pass planText")
            : new PlanRead.Accepted(
                new PlanSource(PlanOrigin.Commit, sha, path, entry.Oid, entry.Size, DocumentRules.ArtifactIdOf(text)), text, string.Empty);
    }

    /// <summary>
    /// The sentence for a read that cannot be believed, or empty. In this order: did it finish, did it succeed, was
    /// the capture whole — the flag is refused even when the id would match, because it is evidence the id cannot see.
    /// </summary>
    private static string CaptureGate(GitAnswer answer, string what)
    {
        if (!answer.Ran)
        {
            return $"git did not finish {what} within 30 s — re-run when the machine is less busy";
        }

        if (!answer.Ok)
        {
            return $"git could not complete {what} — the commit or the object is not in this repository, or the object database is damaged; run git fsck";
        }

        return answer.Truncated
            ? $"{what} was cut short — an incomplete read; nothing was reviewed; re-run, and if it repeats report it"
            : string.Empty;
    }

    private static string NotSha1(string sha) =>
        $"commit {sha} is {sha.Length} hex characters — committedPlan reads SHA-1 repositories (40-hex ids) only; for a SHA-256 repository pass planText";

    private static string Short(string id) => id[..Math.Min(12, id.Length)];
}
