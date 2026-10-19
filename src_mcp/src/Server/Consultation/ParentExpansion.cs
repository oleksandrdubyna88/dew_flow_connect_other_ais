using System.Globalization;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>A source of a one-shot place as the place first met it: its normalised path, and what git said about it.</summary>
/// <param name="Index">The grant's place in the row's list — the folder its copies stand under.</param>
/// <param name="Root">The full path without a trailing separator (<see cref="GrantedPaths.Keyed"/>) — what every later step checks, asks and walks.</param>
/// <param name="Facts">What one git call said about it: a work tree is copied as it is, anything else is a parent to expand.</param>
/// <param name="Duplicate">A work tree an earlier grant already names — never copied twice.</param>
internal sealed record GrantedFolder(int Index, string Root, RepositoryFacts Facts, bool Duplicate)
{
    public bool IsWorkTree => Facts.InsideWorkTree;
}

/// <summary>What expanding every granted parent came to: why the row is refused, or, per grant, the repositories to copy.</summary>
internal sealed record Expanded(string Refusal, IReadOnlyDictionary<int, IReadOnlyList<FoundRepository>> ToCopy)
{
    public static Expanded Refused(string reason) => new(reason, new Dictionary<int, IReadOnlyList<FoundRepository>>());
}

/// <summary>
/// A granted root that is NOT a work tree, expanded into the repositories beneath it (D1–D7 and D9 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>): the walk, git's confirmation of each candidate as the TOP of a work tree,
/// the linked worktrees left out, the bound — all of it over the WHOLE row before the first copy — and then each selected
/// repository through the one copy method.
/// </summary>
/// <remarks>
/// <para>The order is the contract: every candidate of every parent is validated, THEN linked worktrees whose main checkout the row
/// copies are selected out, THEN the bound counts what is left — so the bound is the row's, the budget's "of" is the real total, and
/// a refusal comes before anything is made. A candidate git does not confirm is SKIPPED with a note; a copy that is refused REFUSES
/// THE ROW, and nothing here catches it.</para>
/// <para>Every refusal says what the walk had skipped before it, through <see cref="OneShotTally.Say"/>, so the row's note keeps it.</para>
/// </remarks>
/// <param name="listing">The walk's listing of the root and its children — a test's seam.</param>
/// <param name="probing">The walk's look for a <c>.git</c> at the deepest level — a test's seam.</param>
/// <param name="beforeCopyOf">Called with a repository's path right before its copy — a test's seam for a folder that becomes a link in between.</param>
internal sealed class ParentExpansion(
    IProcessLauncher launcher,
    RepositoryCopier copier,
    Func<string, FolderListing> listing,
    Func<string, FolderListing> probing,
    Action<string> beforeCopyOf)
{
    /// <summary>What validating one root came to: its confirmed candidates, or the sentence that refuses the row.</summary>
    private sealed record Discovered(string Refusal, int Index, IReadOnlyList<ConfirmedRepository> Candidates);

    /// <summary>
    /// Validates and selects for every granted parent of <paramref name="grants"/> (those git did not call a work tree), in source order.
    /// Directly granted work trees are already in <paramref name="tally"/>: counted, and their main checkouts known.
    /// </summary>
    public async Task<Expanded> PrepareAsync(IReadOnlyList<GrantedFolder> grants, OneShotTally tally, CancellationToken ct)
    {
        List<Discovered> found = [];
        foreach (var grant in grants.Where(grant => !grant.IsWorkTree))
        {
            var discovered = await DiscoverAsync(grant, tally, ct);
            if (discovered.Refusal.Length > 0)
            {
                return Expanded.Refused(discovered.Refusal);
            }

            found.Add(discovered);
        }

        return Selected(found, tally);
    }

    /// <summary>One root: the link check, the walk (its notes said), then each candidate asked of git — until the bound is certainly passed.</summary>
    private async Task<Discovered> DiscoverAsync(GrantedFolder grant, OneShotTally tally, CancellationToken ct)
    {
        if (OwnedPaths.IsLinkIfThere(grant.Root))
        {
            return Refusal(GrantedPaths.LinkSentence(grant.Root, grant.Root));
        }

        var walked = RepositoryWalk.Find(grant.Root, listing, probing, ct);
        foreach (var skipped in walked.Notes)
        {
            tally.Say(skipped);
        }

        return walked is WalkOutcome.Found repositories
            ? await ConfirmedAsync(grant, repositories.Repositories, tally, ct)
            : Refusal(((WalkOutcome.Refused)walked).Reason);
    }

    private static Discovered Refusal(string reason) => new(reason, -1, []);

    /// <summary>
    /// Discovery's side of D4: each candidate's links checked, then git asked once — in order. One broken checkout never costs the
    /// others; a candidate that is not the TOP of a work tree is skipped with a note that says which cause it was.
    /// </summary>
    private async Task<Discovered> ConfirmedAsync(GrantedFolder grant, IReadOnlyList<FoundRepository> candidates, OneShotTally tally, CancellationToken ct)
    {
        tally.Candidates += candidates.Count;
        List<ConfirmedRepository> confirmed = [];
        foreach (var repository in candidates)
        {
            var step = await ConfirmOneAsync(grant, repository, tally, ct);
            if (step.Refusal.Length > 0)
            {
                return Refusal(step.Refusal);
            }

            confirmed.AddRange(step.Confirmed);
        }

        return confirmed.Count == 0 ? Refusal(RepositoryWalk.NoRepository(grant.Root)) : new Discovered(string.Empty, grant.Index, confirmed);
    }

    /// <summary>What confirming ONE candidate came to: a refusal of the row, the candidate confirmed, or — both empty — the candidate skipped.</summary>
    private sealed record Step(string Refusal, IReadOnlyList<ConfirmedRepository> Confirmed);

    /// <summary>One candidate: its links checked, git asked, then taken (and counted) or skipped with its note.</summary>
    private async Task<Step> ConfirmOneAsync(GrantedFolder grant, FoundRepository repository, OneShotTally tally, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (GrantedPaths.LinkUpTo(repository.Path, grant.Root) is { Length: > 0 } link)
        {
            return new Step(GrantedPaths.LinkSentence(link, grant.Root), []);
        }

        var facts = await RepositoryFacts.AskAsync(launcher, repository.Path, ct);

        // A confirmation cut short by the budget comes back as "not a work tree": the budget is what ended it, and says so.
        ct.ThrowIfCancellationRequested();

        return facts.InsideWorkTree && facts.IsTop ? Taken(new ConfirmedRepository(repository, facts), tally) : Skipped(repository, facts, tally);
    }

    private static Step Taken(ConfirmedRepository candidate, OneShotTally tally) =>
        new(Counted(candidate, tally) ? RepositoryWalk.TooManyRepositories : string.Empty, [candidate]);

    private static Step Skipped(FoundRepository repository, RepositoryFacts facts, OneShotTally tally)
    {
        tally.Say(facts.SkippedBecause(repository.Path));

        return new Step(string.Empty, []);
    }

    /// <summary>
    /// Takes the candidate into what the row knows — a main checkout is remembered (D9), a repository no selection can remove is
    /// counted — and whether the bound is now certainly passed. A linked worktree may still be selected out, so it counts for nothing yet.
    /// </summary>
    private static bool Counted(ConfirmedRepository candidate, OneShotTally tally)
    {
        if (candidate.Facts.IsMain)
        {
            tally.Mains.Add(candidate.Facts.CommonDir);
        }

        var key = GrantedPaths.Keyed(candidate.Repository.Path);
        if (!candidate.Facts.IsLinked && !tally.Planned.Contains(key))
        {
            tally.Certain.Add(key);
        }

        return tally.Found + tally.Certain.Count > RepositoryWalk.MaxRepositories;
    }

    /// <summary>
    /// Every root's candidates, now that every main checkout is known: the linked worktrees whose main is copied left out (one note for
    /// the row), the rest planned in source order — first grant wins, a directly granted repository before them all — and counted.
    /// </summary>
    private static Expanded Selected(IReadOnlyList<Discovered> found, OneShotTally tally)
    {
        Dictionary<int, IReadOnlyList<FoundRepository>> toCopy = [];
        foreach (var root in found)
        {
            var fresh = root.Candidates.Where(candidate => IsNew(candidate, tally)).ToArray();
            var selection = LinkedWorktrees.Select(fresh, tally.Mains);
            selection.Kept.ToList().ForEach(candidate => tally.Planned.Add(GrantedPaths.Keyed(candidate.Repository.Path)));
            tally.LeftOut.UnionWith(selection.Left.Select(candidate => GrantedPaths.Keyed(candidate.Repository.Path)));
            tally.Found += selection.Kept.Count;
            toCopy[root.Index] = [.. selection.Kept.Select(candidate => candidate.Repository)];
        }

        return Said(toCopy, tally);
    }

    /// <summary>A candidate no grant has planned and no earlier root has left out.</summary>
    private static bool IsNew(ConfirmedRepository candidate, OneShotTally tally)
    {
        var key = GrantedPaths.Keyed(candidate.Repository.Path);

        return !tally.Planned.Contains(key) && !tally.LeftOut.Contains(key);
    }

    /// <summary>The summary of what was left out, said once — then the bound, judged over what is left to copy.</summary>
    private static Expanded Said(Dictionary<int, IReadOnlyList<FoundRepository>> toCopy, OneShotTally tally)
    {
        if (LinkedWorktrees.Sentence(tally.LeftOut.Count) is { Length: > 0 } sentence)
        {
            tally.Limit(sentence);
        }

        return tally.Found > RepositoryWalk.MaxRepositories
            ? Expanded.Refused(RepositoryWalk.TooManyRepositories)
            : new Expanded(string.Empty, toCopy);
    }

    /// <summary>Copy's side of D4: every selected repository of one grant through the one copy method; the first refusal refuses the row.</summary>
    public async Task<string> CopyAsync(GrantedFolder grant, IReadOnlyList<FoundRepository> repositories, string tree, OneShotTally tally, CancellationToken ct)
    {
        foreach (var repository in repositories)
        {
            beforeCopyOf(repository.Path);
            var destination = Path.Combine([tree, grant.Index.ToString(CultureInfo.InvariantCulture), .. repository.Segments]);
            var refused = tally.Record(await copier.CopyAsync(repository.Path, destination, grant.Root, ct), ct);
            if (refused.Length > 0)
            {
                return refused;
            }
        }

        return string.Empty;
    }
}
