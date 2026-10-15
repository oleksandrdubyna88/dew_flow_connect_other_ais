using CoaiMcp.Core;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;

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
/// <para><see cref="ReadAsync"/> is that proof over an already-canonical path and reads the OBJECT only.
/// <see cref="ReadGuardedAsync"/> is what a caller names a plan by: it canonicalises the path and refuses a bad or
/// credential-shaped one before any process, finds the repository root and HEAD, and — when HEAD IS the commit being
/// read — refuses a plan that is edited or staged in the working tree, because the reviewers would read the commit while
/// the caller believed they read the edit. When HEAD is another commit the working tree says nothing about this one, so
/// nothing is compared and the note says so.</para>
/// <para>Two blind spots, said rather than fixed: a path marked <c>skip-worktree</c> or <c>assume-unchanged</c> (a sparse
/// checkout) reads as clean, and a branch checked out in ANOTHER worktree than <c>repoPath</c> is "not compared" — true,
/// since that tree is not this one. A caller that must assert WHICH commit it reviewed passes <c>expectedCommit</c>.</para>
/// </remarks>
public static class CommittedPlanReader
{
    public static async Task<PlanRead> ReadGuardedAsync(
        GitHistory git, string repoPath, string sha, string path, CancellationToken ct)
    {
        var check = PlanPath.Check(path);
        var refusal = check.Refusal.Length > 0 ? check.Refusal : ShaRefusal(sha);

        return refusal.Length > 0
            ? new PlanRead.Refused(refusal)
            : await PlacedAsync(git, repoPath, sha, check.Path, ct);
    }

    /// <summary>Where the checkout's root is and what HEAD is — then the proof, then (when HEAD is the commit) the status.</summary>
    private static async Task<PlanRead> PlacedAsync(GitHistory git, string repoPath, string sha, string path, CancellationToken ct)
    {
        var placed = Place(await git.RootAndHeadAsync(repoPath, ct));
        if (placed.Refusal.Length > 0)
        {
            return new PlanRead.Refused(placed.Refusal);
        }

        var read = await ReadAsync(git, repoPath, sha, path, ct);

        return read is PlanRead.Accepted accepted ? await CleanAsync(git, placed, sha, accepted, ct) : read;
    }

    private static async Task<PlanRead> CleanAsync(GitHistory git, Placement placed, string sha, PlanRead.Accepted accepted, CancellationToken ct)
    {
        if (!placed.Head.Equals(sha, StringComparison.OrdinalIgnoreCase))
        {
            return accepted with { Note = NotCompared(placed.Head, sha) };
        }

        var dirt = Dirt(await git.PathStatusAsync(placed.Root, accepted.Source.Path, ct), accepted.Source.Path);

        return dirt.Length > 0 ? new PlanRead.Refused(dirt) : accepted;
    }

    /// <summary>The sentence for an edited or staged path, or empty. Staged first: it is the one a plain <c>git commit</c> does not cover.</summary>
    private static string Dirt(GitAnswer status, string path)
    {
        var gate = CaptureGate(status, $"asking whether '{path}' is edited");
        if (gate.Length > 0)
        {
            return gate;
        }

        var entries = PorcelainReader.Read(status.Out);

        return entries.Count == 0 ? string.Empty
            : entries[0].Code[0] != ' ' ? $"'{path}' has staged changes — the reviewers read the commit, not your edit; commit them (git commit) and call again"
            : $"'{path}' has uncommitted changes in the working tree — the reviewers read the commit, not your edit; commit them and call again";
    }

    private static Placement Place(GitAnswer answer)
    {
        var gate = CaptureGate(answer, "finding the repository root and HEAD");
        var lines = answer.Lines;

        return gate.Length > 0 ? new Placement(string.Empty, string.Empty, gate)
            : lines.Count == 2 ? new Placement(lines[0], lines[1], string.Empty)
            : new Placement(string.Empty, string.Empty, $"git rev-parse answered {lines.Count} lines for the root and HEAD, not 2 — report it with the git version");
    }

    private static string NotCompared(string head, string sha) =>
        $"the working tree was not compared: HEAD is {Short(head)} and the plan was read at {Short(sha)}";

    private sealed record Placement(string Root, string Head, string Refusal);

    public static async Task<PlanRead> ReadAsync(
        GitHistory git, string repoPath, string sha, string canonicalPath, CancellationToken ct)
    {
        var unreadable = Unreadable(sha, canonicalPath);
        if (unreadable.Length > 0)
        {
            return new PlanRead.Refused(unreadable);
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

    /// <summary>What makes a read impossible before any process: a commit that is not 40 hex, or a path that is not in the repository.</summary>
    private static string Unreadable(string sha, string path)
    {
        if (!GitHistory.IsCommitish(sha))
        {
            return NotSha1(sha);
        }

        var why = RepoPaths.WhyNotRelative(path);

        return why.Length > 0 ? $"'{path}' {why} — pass a path relative to the repository root, as committed (todo/PLAN_x.md)" : string.Empty;
    }

    private static string ShaRefusal(string sha) => GitHistory.IsCommitish(sha) ? string.Empty : NotSha1(sha);

    private static string NotSha1(string sha) =>
        $"commit {sha} is {sha.Length} hex characters — committedPlan reads SHA-1 repositories (40-hex ids) only; for a SHA-256 repository pass planText";

    private static string Short(string id) => id[..Math.Min(12, id.Length)];
}
