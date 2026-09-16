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

/// <summary>Deciding what became of one candidate — the seam a run is driven through.</summary>
/// <remarks>
/// One method, because there is one question. It exists for the reason every other seam in this
/// repository exists (<c>IProcessLauncher</c>, <c>IAstNormalizer</c>): the paths worth testing here
/// are the ones where the work FAILS, and a run whose collector cannot be made to throw has a
/// `finally` nothing can prove. The testing rule says to make a defect testable rather than to skip
/// it, and this is the cheapest way to do that without a second implementation of anything.
/// </remarks>
public interface ICollector
{
    /// <summary>What became of this candidate.</summary>
    Task<CollectOutcome> CollectAsync(Candidate candidate, CancellationToken ct = default);
}

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
public sealed class Collector(GitHistory git, IAstNormalizer normalizer) : ICollector
{
    /// <summary>How far a walk will follow a file's history before giving up.</summary>
    /// <remarks>
    /// A file with ten thousand commits would otherwise spend one candidate's whole budget on a
    /// history nobody reads to the end of, and the collector's contract is that a run finishes. Past
    /// the bound the answer is an honest <c>fix_commit_not_found</c>: not found WITHIN the bound is
    /// what it says, and the state is text so a later run with a wider one can revisit it.
    /// </remarks>
    private const int WalkCap = 200;

    /// <summary>
    /// Whether a path is a repository, remembered for the run.
    /// </summary>
    /// <remarks>
    /// A batch of candidates out of one checkout asked <c>git rev-parse --git-dir</c> once per
    /// candidate — process startup repeated for an answer that depends only on the path. Keyed by the
    /// canonical path, because the same repository is recorded three ways in the live database.
    /// (Code round, codex.)
    /// </remarks>
    private readonly Dictionary<string, bool> _repositories = new(StringComparer.Ordinal);

    /// <summary>What became of this candidate.</summary>
    public async Task<CollectOutcome> CollectAsync(Candidate candidate, CancellationToken ct = default)
    {
        if (CandidatePath.IsTransient(candidate.RepoPath))
        {
            return CollectOutcome.Skip(SkipReason.RepoPathTransient);
        }

        var repository = await IsRepositoryAsync(candidate.RepoPath, ct);
        if (!repository.Ran)
        {
            // A probe that did not finish says nothing about the path. Recording it as missing would
            // mark the candidate processed and bury an outage in the skip funnel.
            return CollectOutcome.Fail(SkipReason.GitFailed);
        }

        if (!repository.Ok)
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

    /// <summary>The repository probe, asked once per canonical path per run.</summary>
    private async Task<GitAnswer> IsRepositoryAsync(string repoPath, CancellationToken ct)
    {
        var key = CandidatePath.Canonical(repoPath);
        if (_repositories.TryGetValue(key, out var known))
        {
            return new GitAnswer(true, known, string.Empty);
        }

        var answer = await git.IsRepositoryAsync(repoPath, ct);
        if (answer.Ran)
        {
            // Only a COMPLETED probe is remembered: caching a timeout would turn one slow moment
            // into every candidate in that repository being wrong for the rest of the run.
            _repositories[key] = answer.Ok;
        }

        return answer;
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

            // A git FAILURE judging the interval is not "there is no interval". Falling through to
            // the refs here turned a timeout into an open-ended walk down whichever branch happened
            // to contain the commit — a different search, reported as though it were this one.
            // Every other git failure in this class is a `failed`; this one silently was not.
            // (Code round, gemini.)
            if (later.Ok)
            {
                return await DescendsAsync(candidate, candidate.LaterSha, ct) switch
                {
                    true => (candidate.LaterSha, null),
                    // It exists but does not descend — a stale row, or a session whose later round
                    // was taken on another line of work. The open-ended walk is the honest fallback.
                    false => await OpenEndedAsync(candidate, ct),
                    _ => (string.Empty, CollectOutcome.Fail(SkipReason.GitFailed)),
                };
            }
        }

        return await OpenEndedAsync(candidate, ct);
    }

    /// <summary>The end of an unbounded search: a ref that DESCENDS from the broken commit.</summary>
    /// <remarks>
    /// Which refs CONTAIN the commit, never which refs exist — walking a ref that does not descend
    /// from it would attribute somebody else's edit as the fix. No ref containing it means there is
    /// no history to walk forward through at all, which is what `head_sha_orphaned` says.
    /// </remarks>
    private async Task<(string To, CollectOutcome? Outcome)> OpenEndedAsync(
        Candidate candidate, CancellationToken ct)
    {
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
    /// <summary>
    /// The session's own branch when it descends, else an integration branch, else any ref.
    /// </summary>
    /// <remarks>
    /// <para><b>Exact ref names, not a suffix.</b> `EndsWith("/" + branch)` matches `refs/heads/main`
    /// for a branch called `main` and also `refs/heads/feature/main` for one called `main` — and an
    /// empty branch name matched everything ending in a slash. (Code round, gemini.)</para>
    /// <para><b>And an integration branch before an arbitrary one.</b> The fallback took `refs[0]`,
    /// which is whatever `for-each-ref` happened to print first — a dead tag as readily as the line
    /// of work the fix actually landed on. (Code round, gemini.)</para>
    /// </remarks>
    private static string Preferred(IReadOnlyList<string> refs, string branch)
    {
        var wanted = branch.Length == 0
            ? []
            : (string[])[$"refs/heads/{branch}", $"refs/remotes/origin/{branch}"];

        foreach (var name in wanted.Concat(Integration))
        {
            if (refs.Contains(name, StringComparer.Ordinal))
            {
                return name;
            }
        }

        return refs[0];
    }

    /// <summary>Where fixes land when the branch that found them is gone.</summary>
    private static readonly string[] Integration =
    [
        "refs/heads/main", "refs/remotes/origin/main",
        "refs/heads/master", "refs/remotes/origin/master",
        "refs/heads/trunk", "refs/remotes/origin/trunk",
    ];

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
        var (ran, commits) = await git.CommitsTouchingAsync(
            candidate.RepoPath, candidate.HeadSha, to, candidate.File, WalkCap, ct);

        if (!ran)
        {
            return CollectOutcome.Fail(SkipReason.GitFailed);
        }

        if (commits.Count == 0)
        {
            return CollectOutcome.Skip(SkipReason.FixCommitNotFound);
        }

        var skeletonBefore = normalizer.Normalise(language, symbol.Source);
        var everFound = false;

        foreach (var touched in commits)
        {
            // The path AT THAT COMMIT, not today's: `--follow` reports commits from before a rename.
            var later = await git.FileAtAsync(candidate.RepoPath, touched.Sha, touched.Path, ct);
            if (!later.Ran)
            {
                return CollectOutcome.Fail(SkipReason.GitFailed);
            }

            if (!later.Ok)
            {
                continue;
            }

            // An overload set shares a name, so a name is not an identity. Refusing an ambiguous one
            // is the safe direction: comparing the wrong overload would record an unrelated commit as
            // this defect's fix, with a sha to prove it. (Code round, codex, twice.)
            if (normalizer.CountNamed(language, later.Out, symbol.Name) > 1)
            {
                return CollectOutcome.Skip(SkipReason.SymbolAmbiguous);
            }

            if (normalizer.LocateNamed(language, later.Out, symbol.Name) is not { } moved)
            {
                continue;
            }

            everFound = true;
            var skeletonAfter = normalizer.Normalise(language, moved.Source);
            if (!string.Equals(skeletonAfter, skeletonBefore, StringComparison.Ordinal))
            {
                return new CollectOutcome(
                    CollectState.Collected, [], touched.Sha, symbol.Name, skeletonBefore, skeletonAfter,
                    language.ToString());
            }
        }

        return CollectOutcome.Skip(everFound ? SkipReason.MethodUnchanged : SkipReason.SymbolGone);
    }
}
