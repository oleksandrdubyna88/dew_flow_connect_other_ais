using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>What reconstructing one stored code round's size came to: a size, or the closed reason it cannot be had.</summary>
internal abstract record SizeOutcome
{
    public const string RepositoryNotFound = "repository not found";
    public const string BaseNotFound = "commit not found: base";
    public const string HeadNotFound = "commit not found: head";
    public const string BaseNotAnAncestor = "size: base not an ancestor";

    /// <summary>The diff could not be taken though both commits are there: git refused (a damaged repository, a timeout).</summary>
    public const string GitFailed = "git failed";

    private SizeOutcome()
    {
    }

    /// <summary>The size, marked <see cref="SizeOrigin.Backfill"/>, with the count of changed submodule pointers.</summary>
    public sealed record Measured(RoundSize Size, int? Gitlinks) : SizeOutcome;

    /// <summary>Nothing is written for the round; the summary counts <paramref name="Reason"/>.</summary>
    public sealed record Unrecoverable(string Reason) : SizeOutcome;
}

/// <summary>
/// A stored code round's size, reconstructed the way the live round measured it and never re-based (E7.S2 of
/// <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md</c>): the diff the round would have built from its RECORDED base and head, shaped
/// with today's budget, and measured by <see cref="RoundSizer"/> — the live assembly, so the partition, the delta numstat, <c>numstats disagree</c> and
/// the gitlink count are E2's own code, not a second copy.
/// </summary>
/// <remarks>
/// <para><b>The whole scope's <c>From</c> is <c>rounds.base_ref</c>, never a merge-base computed today.</b> <c>base_ref</c> is the base the live diff
/// was resolved against (<c>CollectedDiff.ComparedAgainst</c>); after the branch merged, <c>merge-base(main, sha)</c> is <c>sha</c> itself and the whole
/// would be zero. <see cref="ContextAssembler.CollectAsync"/> resolves its comparison base from what it is handed, so handed <c>base_ref</c> — an
/// ancestor of the head — it resolves to <c>base_ref</c>; a result whose <c>ComparedAgainst</c> is anything else is not kept
/// (<see cref="SizeOutcome.BaseNotAnAncestor"/>): the live round's two-tip fallback, or a rewritten history, so no size is reconstructed rather
/// than a new base guessed.</para>
/// <para>The delta's predecessor is <see cref="DeltaPredecessor"/>'s, fed the journal's rounds BEFORE this one — each carrying the size the run
/// already gave it.</para>
/// </remarks>
internal sealed class StoredSizer(ContextAssembler context, IProcessLauncher launcher, Serilog.ILogger log)
{
    public async Task<SizeOutcome> MeasureAsync(StoredRound round, string repoPath, IReadOnlyList<RoundRecord> earlier, CancellationToken ct)
    {
        var absent = await Missing(repoPath, round, ct);
        if (absent.Length > 0)
        {
            return new SizeOutcome.Unrecoverable(absent);
        }

        try
        {
            return await Reconstruct(round, repoPath, earlier, ct);
        }
        catch (ContextException)
        {
            return new SizeOutcome.Unrecoverable(SizeOutcome.GitFailed);
        }
    }

    private async Task<SizeOutcome> Reconstruct(StoredRound round, string repoPath, IReadOnlyList<RoundRecord> earlier, CancellationToken ct)
    {
        var collected = await context.CollectAsync(repoPath, round.BaseRef, round.HeadSha, ct: ct);
        if (!string.Equals(collected.ComparedAgainst, round.BaseRef, StringComparison.OrdinalIgnoreCase))
        {
            return new SizeOutcome.Unrecoverable(SizeOutcome.BaseNotAnAncestor);
        }

        var measured = await RoundSizer.MeasureAsync(context, repoPath, round.HeadSha, collected, DiffShaper.Shape(collected.Files), earlier, log, ct);

        return measured.Size is { } size
            ? new SizeOutcome.Measured(size with { Origin = SizeOrigin.Backfill }, measured.Gitlinks)
            : new SizeOutcome.Unrecoverable(SizeOutcome.GitFailed);
    }

    private async Task<string> Missing(string repoPath, StoredRound round, CancellationToken ct)
    {
        if (!Directory.Exists(repoPath))
        {
            return SizeOutcome.RepositoryNotFound;
        }

        return !await IsCommit(repoPath, round.BaseRef, ct) ? SizeOutcome.BaseNotFound : !await IsCommit(repoPath, round.HeadSha, ct) ? SizeOutcome.HeadNotFound : string.Empty;
    }

    /// <summary>A commit that is there. A value that is not a hex object name never reaches a command line — a "ref" that begins with a dash is an option.</summary>
    private async Task<bool> IsCommit(string repoPath, string sha, CancellationToken ct) =>
        IsObjectName(sha) && (await launcher.RunAsync(new ProcessRequest("git", ["cat-file", "-e", $"{sha}^{{commit}}"], repoPath), ct)).ExitCode == 0;

    private static bool IsObjectName(string value) => value is { Length: >= 7 and <= 64 } && value.All(Uri.IsHexDigit);
}
