using System.Diagnostics;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Server;

/// <summary>What a code round measured of itself: its size (null when it has none) and how many submodule pointers it changed.</summary>
internal sealed record MeasuredSize(RoundSize? Size, int? Gitlinks);

/// <summary>
/// A live code round's <see cref="RoundSize"/> (E2.S4 of <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md</c>):
/// assembled from the diff the round actually built and shaped, never from a second look at the repository.
/// </summary>
/// <remarks>
/// <para><b>Best-effort, like every projection.</b> A size that cannot be measured is a recorded <c>unavailable: …</c> state and
/// never a failure of the round: every exception but a cancellation becomes <see cref="ScopeState.GitFailed"/> for the scope
/// it hit, and a non-empty <see cref="ChangePartition.Disagreement"/> is a STATE, logged once at Warning with its fixed sentence
/// and no path. The round token still propagates. Every git call is bounded by <see cref="ContextAssembler.SizeGitTimeout"/>.</para>
/// <para><b>Row 0 first.</b> When the round's diff was not taken against a merge-base (<c>collected.Kind != MergeBase</c>:
/// unrelated or truncated histories), <c>against</c> is the base's TIP and the diff would count the base's own commits as
/// deletions — both scopes read <see cref="ScopeState.NoMergeBase"/> and no git process runs.</para>
/// </remarks>
internal static class RoundSizer
{
    /// <summary>The deadline of the WHOLE sizing — every scope together — after which the unfinished scopes read git-failed and the round goes on.</summary>
    internal static readonly TimeSpan SizeTotalTimeout = TimeSpan.FromSeconds(15);

    private const string WholeScope = "whole";
    private const string DeltaScope = "delta";

    /// <param name="earlier">The gate session's trail BEFORE this round — <c>session.Rounds</c> as the work receives it; never re-loaded.</param>
    public static async Task<MeasuredSize> MeasureAsync(
        ContextAssembler context,
        string repoPath,
        string sha,
        CollectedDiff collected,
        ShapedDiff shaped,
        IReadOnlyList<RoundRecord> earlier,
        Serilog.ILogger log,
        CancellationToken ct,
        TimeSpan? total = null)
    {
        var watch = Stopwatch.StartNew();
        var predecessor = DeltaPredecessor.Choose(earlier, collected.ComparedAgainst);
        // The sizing's OWN deadline, linked to the round token so a cancelled round still stops it — but a token of its own, so
        // that its expiry is told apart from the round's cancellation (see GuardedAsync).
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(total ?? SizeTotalTimeout);
        var measured = collected.Kind == DiffBase.MergeBase
            ? await MeasureRangeAsync(context, repoPath, sha, collected, shaped, predecessor, log, new Limits(ct, budget.Token))
            : WithoutAMergeBase(collected.ComparedAgainst, predecessor);
        Report(log, measured, watch.ElapsedMilliseconds);

        return measured;
    }

    /// <summary>S1.5 row 0: both scopes unavailable, decided from the kind alone — no git call.</summary>
    private static MeasuredSize WithoutAMergeBase(string against, Predecessor predecessor) =>
        new(new RoundSize(
            ScopeSize.Unavailable(ScopeState.NoMergeBase, against),
            ScopeSize.Unavailable(ScopeState.NoMergeBase, predecessor.Sha)), null);

    /// <summary>The two tokens a sizing answers to: the ROUND's, which propagates, and the sizing's own total deadline, which does not.</summary>
    private readonly record struct Limits(CancellationToken Round, CancellationToken Total);

    /// <summary>The whole scope, the delta and the gitlink count are independent, so they run side by side.</summary>
    private static async Task<MeasuredSize> MeasureRangeAsync(
        ContextAssembler context,
        string repoPath,
        string sha,
        CollectedDiff collected,
        ShapedDiff shaped,
        Predecessor predecessor,
        Serilog.ILogger log,
        Limits limits)
    {
        var elided = shaped.Elided.Select(e => e.Path).ToList();
        var whole = GuardedAsync(
            () => WholeAsync(context, repoPath, sha, collected, elided, log, limits.Total),
            ScopeSize.Unavailable(ScopeState.GitFailed, collected.ComparedAgainst), log, limits);
        var delta = GuardedAsync(
            () => DeltaAsync(context, repoPath, sha, predecessor, elided, log, limits.Total),
            ScopeSize.Unavailable(ScopeState.GitFailed, predecessor.Sha), log, limits);
        var gitlinks = GuardedAsync(
            () => context.GitlinksAsync(repoPath, collected.ComparedAgainst, sha, limits.Total), (int?)null, log, limits);
        await Task.WhenAll(whole, delta, gitlinks);

        return new MeasuredSize(new RoundSize(await whole, await delta), await gitlinks);
    }

    private static async Task<ScopeSize> WholeAsync(
        ContextAssembler context,
        string repoPath,
        string sha,
        CollectedDiff collected,
        List<string> elided,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        var all = await context.AllChangesAsync(repoPath, collected.ComparedAgainst, sha, ct);

        return all.Failed
            ? ScopeSize.Unavailable(ScopeState.GitFailed, collected.ComparedAgainst)
            : Checked(log, WholeScope, collected.Counted, all.Changes, elided, ChangePartition.Of(collected.Counted, all.Changes, elided, collected.ComparedAgainst));
    }

    private static async Task<ScopeSize> DeltaAsync(
        ContextAssembler context,
        string repoPath,
        string sha,
        Predecessor predecessor,
        List<string> elided,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        if (!predecessor.IsChosen)
        {
            return ScopeSize.Unavailable(predecessor.State, predecessor.Sha);
        }

        var range = await context.DeltaAsync(repoPath, predecessor.Sha, sha, null, ct);

        return range.State != ScopeState.Recorded
            ? ScopeSize.Unavailable(range.State, predecessor.Sha)
            : Checked(
                log, DeltaScope, range.Filtered, range.Unfiltered, ChangePartition.NarrowedToDelta(range.Filtered, elided),
                ChangePartition.OfDelta(range.Filtered, range.Unfiltered, elided, predecessor.Sha));
    }

    /// <summary>A partition that refused to be one is logged once, with its fixed sentence (never a path), and returned as the state it is.</summary>
    private static ScopeSize Checked(
        Serilog.ILogger log,
        string scope,
        IReadOnlyList<CountedChange> filtered,
        IReadOnlyList<CountedChange> unfiltered,
        IReadOnlyCollection<string> elided,
        ScopeSize partition)
    {
        if (partition.State == ScopeState.NumstatsDisagree)
        {
            log.Warning("the {Scope} size was not recorded: {Disagreement}", scope, ChangePartition.Disagreement(filtered, unfiltered, elided));
        }

        return partition;
    }

    /// <summary>
    /// One scope's failure is that scope's state, not the round's: everything is caught and logged without a path — except a
    /// cancellation of the ROUND, which propagates. A cancellation that is NOT the round's is the sizing's own total deadline
    /// (or a call that gave up), and the unfinished scope reads <c>fallback</c>; the wait itself is bounded too, so a launcher
    /// that ignores its token cannot hold the sizing past the deadline.
    /// </summary>
    private static async Task<T> GuardedAsync<T>(Func<Task<T>> measure, T fallback, Serilog.ILogger log, Limits limits)
    {
        try
        {
            return await measure().WaitAsync(limits.Total);
        }
        catch (Exception e) when (e is not OperationCanceledException || !limits.Round.IsCancellationRequested)
        {
            log.Warning("a round's size could not be measured: {Failure}", e.GetType().Name);

            return fallback;
        }
    }

    private static void Report(Serilog.ILogger log, MeasuredSize measured, long milliseconds)
    {
        if (measured.Size is { } size)
        {
            log.Information(
                "round size: whole {WholeState} [{Whole}], delta {DeltaState}, {Gitlinks} gitlinks, {Milliseconds} ms",
                size.Whole.State, Describe(size.Whole), size.Delta.State, measured.Gitlinks, milliseconds);
        }
    }

    /// <summary>Files and lines by coverage — a binary has files and no lines, and says so.</summary>
    private static string Describe(ScopeSize scope) =>
        string.Join(", ", scope.Cells.GroupBy(c => c.Coverage).Select(g => g.Key == ChangeCoverage.Binary
            ? $"{g.Key} {g.Sum(c => c.Files)} files"
            : $"{g.Key} {g.Sum(c => c.Files)} files +{g.Sum(c => c.Added)}/-{g.Sum(c => c.Removed)}"));
}
