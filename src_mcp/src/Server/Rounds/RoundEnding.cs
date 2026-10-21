using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>How a round that ran its scheduler ends — decided ONCE, from the recorded first cause and the scheduler's marks (D2, D11).</summary>
public enum RoundEnding
{
    /// <summary>No reviewer was launched yet when the caller's token fired: nothing is recorded (D2, "a cancel during setup").</summary>
    Setup,

    /// <summary>The ordinary ending: the gate judges the results, whatever they are.</summary>
    Completed,

    /// <summary>The caller withdrew the call (or the host stopped) and it ended at least one turn — or the round had no reviewer at all.</summary>
    Withdrawn,
}

/// <summary>The one place the withdraw-or-complete question is asked.</summary>
internal static class RoundEndings
{
    /// <param name="cause">RoundCancellation.CauseAt(the moment the scheduler returned) — the ledger's word.</param>
    /// <param name="anyTurnMarked">results.Any(r => r.Outcome.EndedByRoundToken).</param>
    /// <param name="noReviewer">work.Count == 0 — a round only the security lane's budget admitted (RoundMachine.AdmittedOnlyForTheLane).</param>
    public static RoundEnding Decide(string cause, bool anyTurnMarked, bool noReviewer) =>
        cause is EndCauses.Withdrawn or EndCauses.HostStopped && (anyTurnMarked || noReviewer)
            ? RoundEnding.Withdrawn
            : RoundEnding.Completed;

    /// <summary>
    /// The same decision from what the engine holds when the scheduler returns: the round's recorded first cause read at the moment the
    /// SCHEDULER stamped its return (never a clock read after the await), and whether any turn carries the scheduler's mark.
    /// </summary>
    public static RoundEnding Decide(RoundCancellation cancellation, SchedulerRun run, bool noReviewer) =>
        Decide(cancellation.CauseAt(run.ReturnedAt), run.Results.Any(r => r.Outcome.EndedByRoundToken), noReviewer);
}
