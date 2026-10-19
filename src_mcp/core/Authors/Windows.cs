namespace CoaiMcp.Core.Authors;

/// <summary>The tiling of a gate session's journal into windows, and the rank that decides who owns contested activity (E4.S2).</summary>
public static class Windows
{
    public const string Done = "done";
    public const string Skipped = "skipped";
    public const string Interrupted = "interrupted";

    /// <summary>
    /// A round that will carry an authors snapshot: a plan or a code round that ran (not a skip) and was not interrupted. Every other
    /// round of the journal opens no window — its stretch lands in the next recorded round.
    /// </summary>
    public static bool IsRecorded(GateRound round) =>
        round.Stage is "PlanReview" or "CodeReview" && round.Verdict != Skipped && round.Status != Interrupted;

    /// <summary>
    /// Recorded and <c>done</c>: the CLAIM condition. A recorded round that is not settled is <c>running</c>; it tiles its own gate but
    /// ranks last, claims nothing and bounds no prior end (§S2.2b G1, G2).
    /// </summary>
    public static bool IsSettled(GateRound round) => IsRecorded(round) && round.Status == Done;
}
