using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;

namespace CoaiMcp.Runners.Collecting;

/// <summary>One accepted finding, as the collector needs it.</summary>
/// <param name="LaterSha">
/// The <c>head_sha</c> of the next code round in the same session, or empty. By construction that is
/// the state AFTER this round's fixes, which is what bounds the search to one interval.
/// </param>
public sealed record Candidate(
    string RepoPath,
    string Branch,
    string HeadSha,
    string File,
    int Line,
    string LaterSha = "");

/// <summary>
/// Decides what became of one candidate: the fix, a reason it could not be found, or a failure.
/// </summary>
/// <remarks>
/// <para><b>The order of the stages is the design.</b> A bounded interval is looked for BEFORE the
/// commit's reachability from any ref is judged — because 55.7 % of candidates are orphaned by
/// squash-merge and their objects still survive, so a session whose next round exists can still be
/// walked. Guarding on reachability first would discard those without ever trying, which is most of
/// the corpus. (Plan round, gemini.)</para>
/// <para><b>Every git failure is a FAILURE, never a skip.</b> A timeout tells us nothing about
/// somebody's repository, and filing it as one is how an infrastructure problem hides for a month in
/// the skip rate.</para>
/// </remarks>
public sealed class Collector(GitHistory git, IAstNormalizer normalizer)
{
    /// <summary>What became of this candidate.</summary>
    public async Task<CollectOutcome> CollectAsync(Candidate candidate, CancellationToken ct = default)
    {
        if (CandidatePath.IsTransient(candidate.RepoPath))
        {
            return CollectOutcome.Skip(SkipReason.RepoPathTransient);
        }

        if (!await git.IsRepositoryAsync(candidate.RepoPath, ct))
        {
            return CollectOutcome.Skip(SkipReason.RepoPathMissing);
        }

        var present = await git.HasCommitAsync(candidate.RepoPath, candidate.HeadSha, ct);
        if (!present.Ran)
        {
            return CollectOutcome.Fail(SkipReason.GitFailed);
        }

        return present.Ok
            ? await WithIntervalAsync(candidate, ct)
            : CollectOutcome.Skip(SkipReason.HeadShaUnreachable);
    }

    /// <summary>Finds the interval to search, then searches it.</summary>
    private async Task<CollectOutcome> WithIntervalAsync(Candidate candidate, CancellationToken ct)
    {
        var interval = await IntervalAsync(candidate, ct);

        return interval.Outcome ?? await LocateThenWalkAsync(candidate, interval.To, ct);
    }

    /// <summary>
    /// Where the search ends: the next round's commit when there is one, else a ref that descends.
    /// </summary>
    /// <remarks>
    /// The bounded case is tried first and does not consult a ref at all, which is what keeps an
    /// orphaned commit usable. The open-ended case asks which refs CONTAIN the commit rather than
    /// which refs exist — walking a ref that does not descend from it would attribute somebody else's
    /// edit as the fix.
    /// </remarks>
    private async Task<(string To, CollectOutcome? Outcome)> IntervalAsync(
        Candidate candidate, CancellationToken ct)
    {
        if (candidate.LaterSha.Length > 0)
        {
            var later = await git.HasCommitAsync(candidate.RepoPath, candidate.LaterSha, ct);
            if (!later.Ran)
            {
                return (string.Empty, CollectOutcome.Fail(SkipReason.GitFailed));
            }

            if (later.Ok && await DescendsAsync(candidate, candidate.LaterSha, ct) is true)
            {
                return (candidate.LaterSha, null);
            }
        }

        var containing = await git.RefsContainingAsync(candidate.RepoPath, candidate.HeadSha, ct);
        if (!containing.Ran)
        {
            return (string.Empty, CollectOutcome.Fail(SkipReason.GitFailed));
        }

        var refs = containing.Lines;

        return refs.Count > 0
            ? (Preferred(refs, candidate.Branch), null)
            : (string.Empty, CollectOutcome.Skip(SkipReason.HeadShaOrphaned));
    }

    /// <summary>Whether the candidate's commit is an ancestor of <paramref name="of"/>.</summary>
    private async Task<bool?> DescendsAsync(Candidate candidate, string of, CancellationToken ct)
    {
        var ancestor = await git.IsAncestorAsync(candidate.RepoPath, candidate.HeadSha, of, ct);

        return ancestor.Ran ? ancestor.Ok : null;
    }

    /// <summary>
    /// The session's own branch when it descends, else any ref that does.
    /// </summary>
    /// <remarks>
    /// Preferring the session's branch keeps the walk on the line of work the finding belongs to; the
    /// fallback exists because that branch is usually the one squash-merge deleted.
    /// </remarks>
    private static string Preferred(IReadOnlyList<string> refs, string branch) =>
        refs.FirstOrDefault(name => name.EndsWith('/' + branch, StringComparison.Ordinal)) ?? refs[0];

    /// <summary>Reads the method at the broken commit, then looks for the commit that changed it.</summary>
    private async Task<CollectOutcome> LocateThenWalkAsync(Candidate candidate, string to, CancellationToken ct)
    {
        var language = normalizer.LanguageOf(candidate.File);
        if (language is SourceLanguage.Unsupported)
        {
            return CollectOutcome.Skip(SkipReason.LanguageUnsupported);
        }

        var before = await git.FileAtAsync(candidate.RepoPath, candidate.HeadSha, candidate.File, ct);
        if (!before.Ran)
        {
            return CollectOutcome.Fail(SkipReason.GitFailed);
        }

        if (!before.Ok)
        {
            return CollectOutcome.Skip(SkipReason.FileNotInCommit);
        }

        if (normalizer.Locate(language, before.Out, candidate.Line) is not { } symbol || symbol.Name.Length == 0)
        {
            return CollectOutcome.Skip(SkipReason.SymbolNotResolved);
        }

        return await WalkAsync(candidate, to, language, symbol, ct);
    }

    /// <summary>
    /// Walks forward until a commit changes the method's SHAPE, not merely its text.
    /// </summary>
    /// <remarks>
    /// <para><b>Every commit that touches the file, not the first one.</b> A commit that adjusts an
    /// import or another method in the same file leaves this one untouched, and stopping there would
    /// file the candidate as unchanged while the real fix sat one commit later. (Plan round, gemini
    /// and codex, independently.)</para>
    /// <para><b>And the comparison is of SKELETONS.</b> A rename or a reformat changes a method's
    /// text without changing what it does, and treating the first textual difference as the fix would
    /// record a variable rename as a defect's cure. The normaliser already erases exactly those
    /// differences, so comparing what it produces is the cheapest honest test of "did this change the
    /// shape" that this codebase can make. (Plan round, codex.)</para>
    /// <para>The method is found at each commit BY NAME, because the line has moved.</para>
    /// </remarks>
    private async Task<CollectOutcome> WalkAsync(
        Candidate candidate, string to, SourceLanguage language, EnclosingSymbol symbol, CancellationToken ct)
    {
        var commits = await git.CommitsTouchingAsync(
            candidate.RepoPath, candidate.HeadSha, to, candidate.File, ct);

        if (!commits.Ran)
        {
            return CollectOutcome.Fail(SkipReason.GitFailed);
        }

        if (commits.Lines.Count == 0)
        {
            return CollectOutcome.Skip(SkipReason.FixCommitNotFound);
        }

        var skeletonBefore = normalizer.Normalise(language, symbol.Source);
        var everFound = false;

        foreach (var sha in commits.Lines)
        {
            var later = await git.FileAtAsync(candidate.RepoPath, sha, candidate.File, ct);
            if (!later.Ran)
            {
                return CollectOutcome.Fail(SkipReason.GitFailed);
            }

            if (!later.Ok || normalizer.LocateNamed(language, later.Out, symbol.Name) is not { } moved)
            {
                continue;
            }

            everFound = true;
            var skeletonAfter = normalizer.Normalise(language, moved.Source);
            if (!string.Equals(skeletonAfter, skeletonBefore, StringComparison.Ordinal))
            {
                return new CollectOutcome(
                    CollectState.Collected, [], sha, symbol.Name, skeletonBefore, skeletonAfter);
            }
        }

        return CollectOutcome.Skip(everFound ? SkipReason.MethodUnchanged : SkipReason.SymbolGone);
    }
}
