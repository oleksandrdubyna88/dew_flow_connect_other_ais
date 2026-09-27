using System.Collections.Immutable;
using System.Diagnostics;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// One reviewer's conversation inside its held slot: turn after turn until the continuation stops,
/// the cap is reached, or a turn fails — and exactly ONE terminal outcome (plan §4.9, story S3.2).
/// </summary>
/// <remarks>
/// <para><b>Lives inside <see cref="BoundedScheduler"/>'s launch</b>, so the slot a reviewer holds is
/// held for every turn — one reviewer, one conversation — the rate-limit ladder runs per turn (a 429 on
/// turn 2 retries turn 2), and the per-provider peak never rises for a second turn. The scheduler's
/// stand-down count and the ledger both read the ONE outcome this returns; an intermediate outcome
/// reported on the way would move that count early.</para>
/// <para><b>Time.</b> Each turn runs under its own deadline — the invocation's, which the roster set to
/// the reviewer timeout — and the whole conversation under a cap of <c>timeout × (1 + follow-ups)</c>.
/// A turn that times out, or a cap that is reached, is one terminal <see cref="ReviewerOutcome.TimedOut"/>:
/// every turn's usage kept on it, the earlier turns' findings discarded (a failed later turn is a failed
/// reviewer — falling back to findings made while waiting for source could give a false proceed), and
/// the slot released by the scheduler's <c>finally</c>.</para>
/// <para><b>Usage.</b> Every answered turn is added to <see cref="Earlier"/> BEFORE the next thing that
/// can fail — the continuation's serving and the next launch — so a cancellation anywhere after it still
/// carries the turn's cost out on the abandoned outcome.</para>
/// </remarks>
internal sealed class TurnLoop(
    Func<ReviewerWork, CancellationToken, Task<ReviewerOutcome>> runTurn,
    Action<ReviewerProgress>? onProgress)
{
    private ImmutableList<TurnUsage> _earlier = [];
    private ImmutableList<string> _served = [];

    /// <summary>The turns that answered so far — read by the caller's cancellation catch.</summary>
    public IReadOnlyList<TurnUsage> Earlier => _earlier;

    /// <summary>The one terminal outcome of the whole conversation.</summary>
    /// <exception cref="OperationCanceledException">The round ended; <see cref="Earlier"/> holds what answered before it did.</exception>
    public async Task<ReviewerOutcome> RunAsync(ReviewerWork first, CancellationToken ct)
    {
        // The derived cap, or the stage's own when it set a shorter one on the work (the feature stage's
        // whole-review limit for an api reviewer, `ReviewerWork.ConversationCap`) — the lesser of the two.
        var derived = first.Invocation.Request.Timeout * (1 + first.Continue.FollowUps);
        var cap = first.ConversationCap is { } own && own < derived ? own : derived;
        var clock = Stopwatch.StartNew();
        var work = first;
        for (var turn = 1; ; turn++)
        {
            var (outcome, elapsed) = await TurnAsync(work, clock, cap, ct);
            if (outcome is not ReviewerOutcome.Ok answered)
            {
                return outcome with { EarlierTurns = _earlier };
            }

            var decision = await AfterAsync(work, turn, answered, elapsed, ct);
            if (decision is not TurnDecision.Next next)
            {
                return Terminal(answered, turn);
            }

            work = next.Work;
            _served = _served.Add(next.Note);
            Report(work.Invocation, $"turn {turn} answered; asking turn {turn + 1} — {next.Note}");
        }
    }

    /// <summary>One turn under its own deadline and what is left of the cap; TimedOut when nothing is left.</summary>
    private async Task<(ReviewerOutcome Outcome, TimeSpan Elapsed)> TurnAsync(
        ReviewerWork work, Stopwatch clock, TimeSpan cap, CancellationToken ct)
    {
        var left = cap - clock.Elapsed;
        if (left <= TimeSpan.Zero)
        {
            // Never launched, so it consumed nothing — the one TimedOut whose usage is known to be zero.
            return (new ReviewerOutcome.TimedOut { Usage = Usage.None }, TimeSpan.Zero);
        }

        var started = clock.Elapsed;
        var outcome = await runTurn(Within(work, left), ct);

        return (outcome, clock.Elapsed - started);
    }

    /// <summary>
    /// Asks the continuation — with the answered turn already on the ledger, so a cancellation while
    /// serving keeps its cost. On a stop the turn is taken off again: the terminal outcome carries it.
    /// </summary>
    /// <remarks>
    /// The turn's usage is EVERY launch of it — <see cref="ReviewerOutcome.LastTurnUsage"/>, not
    /// <c>Usage</c>, which is the answering launch's alone. A turn whose first launch answered junk and
    /// whose repair was rate limited carries that billed first launch forward on the ladder's retry as
    /// <see cref="ReviewerOutcome.EarlierLaunches"/>; recording <c>Usage</c> here dropped it from the
    /// turn's ledger line and from the round total whenever the turn continued (found by epic 3's code
    /// round, 2026-09-26). A terminal turn was already read through the same member by the ledger.
    /// </remarks>
    private async Task<TurnDecision> AfterAsync(
        ReviewerWork work, int turn, ReviewerOutcome.Ok answered, TimeSpan elapsed, CancellationToken ct)
    {
        _earlier = _earlier.Add(new TurnUsage(turn, answered.LastTurnUsage, elapsed));
        var decision = await work.Continue.AfterAsync(turn, answered, ct);
        if (decision is TurnDecision.Stop)
        {
            _earlier = _earlier.RemoveAt(_earlier.Count - 1);
        }

        return decision;
    }

    private ReviewerOutcome.Ok Terminal(ReviewerOutcome.Ok answered, int turns) =>
        answered with
        {
            Turns = turns,
            Served = string.Join("; ", _served),
            EarlierTurns = _earlier,
        };

    /// <summary>The launch and its repair, each bounded by what is left of the conversation's cap.</summary>
    private static ReviewerWork Within(ReviewerWork work, TimeSpan left) => work with
    {
        Invocation = Bounded(work.Invocation, left),
        Repair = work.Repair is null ? null : Bounded(work.Repair, left),
    };

    private static ReviewerInvocation Bounded(ReviewerInvocation invocation, TimeSpan left) =>
        invocation.Request.Timeout <= left
            ? invocation
            : invocation with { Request = invocation.Request with { Timeout = left } };

    /// <summary>The note a finished conversation carries: how many turns, and what they served.</summary>
    public static string Note(ReviewerOutcome outcome) =>
        outcome is ReviewerOutcome.Ok { Turns: > 1 } ok ? $"{ok.Turns} turns; source: {ok.Served}" : string.Empty;

    private void Report(ReviewerInvocation invocation, string note)
    {
        try
        {
            onProgress?.Invoke(new ReviewerProgress(invocation.Provider, invocation.Role, "running", Note: note));
        }
        catch (Exception)
        {
            // Reporting is not the work.
        }
    }
}
