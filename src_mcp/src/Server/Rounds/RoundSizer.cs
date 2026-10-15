using System.Diagnostics;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Server;

/// <summary>What a code round measured of itself: its size (null when it has none) and how many submodule pointers it changed.</summary>
internal sealed record MeasuredSize(RoundSize? Size, int? Gitlinks);

/// <summary>
/// A live code round's <see cref="RoundSize"/> (E2.S4 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md</c>):
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
        CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var predecessor = DeltaPredecessor.Choose(earlier, collected.ComparedAgainst);
        var measured = collected.Kind == DiffBase.MergeBase
            ? await MeasureRangeAsync(context, repoPath, sha, collected, shaped, predecessor, log, ct)
            : WithoutAMergeBase(collected.ComparedAgainst, predecessor);
        Report(log, measured, watch.ElapsedMilliseconds);

        return measured;
    }

    /// <summary>S1.5 row 0: both scopes unavailable, decided from the kind alone — no git call.</summary>
    private static MeasuredSize WithoutAMergeBase(string against, Predecessor predecessor) =>
        new(new RoundSize(
            ScopeSize.Unavailable(ScopeState.NoMergeBase, against),
            ScopeSize.Unavailable(ScopeState.NoMergeBase, predecessor.Sha)), null);

    private static async Task<MeasuredSize> MeasureRangeAsync(
        ContextAssembler context,
        string repoPath,
        string sha,
        CollectedDiff collected,
        ShapedDiff shaped,
        Predecessor predecessor,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        var elided = shaped.Elided.Select(e => e.Path).ToList();
        var whole = await GuardedAsync(
            () => WholeAsync(context, repoPath, sha, collected, elided, log, ct),
            ScopeSize.Unavailable(ScopeState.GitFailed, collected.ComparedAgainst), log);
        var delta = await GuardedAsync(
            () => DeltaAsync(context, repoPath, sha, predecessor, elided, log, ct),
            ScopeSize.Unavailable(ScopeState.GitFailed, predecessor.Sha), log);
        var gitlinks = await GuardedAsync(
            () => context.GitlinksAsync(repoPath, collected.ComparedAgainst, sha, ct), (int?)null, log);

        return new MeasuredSize(new RoundSize(whole, delta), gitlinks);
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

    /// <summary>One size call's failure is that scope's state, not the round's: everything but a cancellation is caught, logged without a path.</summary>
    private static async Task<T> GuardedAsync<T>(Func<Task<T>> measure, T fallback, Serilog.ILogger log)
    {
        try
        {
            return await measure();
        }
        catch (Exception e) when (e is not OperationCanceledException)
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
