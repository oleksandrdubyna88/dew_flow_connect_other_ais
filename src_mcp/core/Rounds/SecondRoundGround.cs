namespace CoaiMcp.Core.Rounds;

/// <summary>
/// Why the feature stage's SECOND round may run (D23 of the feature-review plan, S3.4) — and
/// <see cref="None"/> until its first round gave a reason.
/// </summary>
/// <remarks>
/// <para>A feature review is one round unless it needs another, and "needs" is exactly three things:
/// a reviewer failed in round 1 (some or all — the retry asks only the ones that failed), a
/// <c>blocking</c> finding came back (the fix wants every reviewer's eyes again), or the person asked
/// for one. Recorded on the state by <c>RoundMachine.CompleteRound</c> for the first two and by
/// <c>RoundMachine.ApplyPersonsRequest</c> for the third — never by a tool argument — and read by
/// <c>RoundMachine.BeginFeatureRound</c>, which admits a second round on no other evidence.</para>
/// <para>Serialised by NAME into the session file, so a file written before the field existed reads
/// back as <see cref="None"/>: a review that had no ground gets none by being older.</para>
/// </remarks>
public enum SecondRoundGround
{
    None,

    /// <summary>Round 1 had a reviewer failure — some, or every one of them.</summary>
    ReviewerFailure,

    /// <summary>Round 1 carried a finding of <c>blocking</c> severity.</summary>
    BlockingFinding,

    /// <summary>The person asked, through a surface only a person writes.</summary>
    PersonAsked,
}
