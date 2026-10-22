namespace CoaiMcp.Server;

/// <summary>
/// How many withdrawals of one stage have happened in a row, the one being recorded included — the count that keeps a free withdrawal from being a
/// free RESAMPLE of the panel (security judge, 2026-10-22; §13.1 of todo/PLAN_a_cancelled_review_stops_its_round_S2_spec.md).
/// </summary>
/// <remarks>
/// <para>A withdrawal judges nothing and spends no round, so an agent a gate is meant to stop could cancel every round whose partial answers it
/// dislikes and ask again until a draw is clean. The count is the run of <c>cancelled</c> rounds of the stage since the last JUDGED round: a round
/// that ran to a verdict resets it; a skipped round, an interrupted one and another stage's rounds neither add to it nor reset it, since none of them
/// is a panel answering. The person is told when it reaches <see cref="Mention"/>, and at the cap the withdrawal holds the gate as a landed gating
/// finding does.</para>
/// <para>Read from the journal the round BEGAN from (<c>loaded.Rounds</c>), so it is a fact of the saved session and not of this process: a
/// restarted server counts the same.</para>
/// </remarks>
/// <param name="Count">Consecutive withdrawn rounds of the stage, including the one being recorded; at least one.</param>
/// <param name="Cap">The count at which the gate is held (<c>COAI_WITHDRAWALS_BEFORE_HOLD</c>).</param>
internal readonly record struct WithdrawalStreak(int Count, int Cap)
{
    /// <summary>The count at which the person is told, without a hold.</summary>
    public const int Mention = 2;

    /// <summary>Whether the cap is reached: the withdrawal holds the gate for a person.</summary>
    public bool HoldsTheGate => Count >= Cap;

    /// <summary>Whether this withdrawal is the one the person is told about — the second, unless the cap holds first.</summary>
    public bool IsWorthAWord => Count == Mention && !HoldsTheGate;

    /// <summary>The sentence a hold by count puts on the cancelled record.</summary>
    public string HoldNote => $"withdrawn {Count} times in a row — {KeptFindings.HeldForAPerson}";

    /// <param name="rounds">The session's journal as the round began from it — every stage, oldest first.</param>
    /// <param name="stage">The stage name the round is recorded under.</param>
    public static WithdrawalStreak Of(IReadOnlyList<RoundRecord> rounds, string stage, int cap) =>
        new(1 + rounds.Where(r => r.Stage == stage).Reverse().TakeWhile(r => !Judged(r)).Count(r => r.Status == RoundRecord.Cancelled), cap);

    /// <summary>
    /// What the person is told: the hold's notice only when the hold was SAVED (a card bound to a session that never recorded it answers nothing),
    /// else — for the second withdrawal in a row that holds nothing — the informational one.
    /// </summary>
    /// <param name="held">Whether the withdrawal holds the gate, by a landed gating finding or by this count.</param>
    /// <param name="landed">Whether the withdrawn session was saved.</param>
    public WithdrawalNotice NoticeFor(bool held, bool landed) => (held, landed) switch
    {
        (true, true) => WithdrawalNotice.Hold,
        (true, false) => WithdrawalNotice.HoldLost,
        _ => IsWorthAWord ? WithdrawalNotice.Mention : WithdrawalNotice.None,
    };

    /// <summary>A round a panel answered: it ran to a verdict, and was not the skip that records nobody could review.</summary>
    private static bool Judged(RoundRecord round) =>
        round.Status == RoundRecord.Done && round.Verdict != RoundRecord.Skipped;
}

/// <summary>What a withdrawal tells the person.</summary>
public enum WithdrawalNotice
{
    /// <summary>Nothing: a first withdrawal, or one past the mention that is not held.</summary>
    None,

    /// <summary>The informational notice of the second withdrawal in a row.</summary>
    Mention,

    /// <summary>The hold's notice — the gate was held and the hold was saved.</summary>
    Hold,

    /// <summary>The gate was to be held and the save was refused: no notice, the refusal is logged.</summary>
    HoldLost,
}
