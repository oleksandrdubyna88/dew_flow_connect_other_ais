namespace CoaiServer;

/// <summary>Which of three different facts a request's <c>remainingSeconds</c> was.</summary>
public enum StatedBudgetState
{
    /// <summary>The request sent none (or a JSON null): an old client, which the budget rule never refuses.</summary>
    Absent,

    /// <summary>A positive whole number of seconds, taken.</summary>
    Stated,

    /// <summary>Something was sent and it was not a budget: dropped with a note, and the review runs as one with none.</summary>
    Dropped,
}

/// <summary>
/// What a request said about its caller's remaining time — story 4.3 of <c>todo/PLAN_every_round_is_counted.md</c>.
/// </summary>
/// <remarks>
/// <para><b>Absent and dropped are different facts, so they are different states.</b> Both leave the job without a
/// client deadline, and the job runs exactly as an old client's would — but the record says which it was, and why a
/// dropped value was dropped, rather than rendering both as a bare <c>0</c> (the epic-4 code round; the family rule that
/// "not captured" and "empty" are never the same value).</para>
/// </remarks>
/// <param name="State">Which fact it is.</param>
/// <param name="Seconds">The stated seconds — positive when <see cref="StatedBudgetState.Stated"/>, else <c>0</c>.</param>
/// <param name="Reason">Why a dropped value was dropped — the same sentence the request's note carries; empty otherwise.</param>
public sealed record StatedBudget(StatedBudgetState State, int Seconds, string Reason)
{
    /// <summary>The request said nothing about its remaining time.</summary>
    public static StatedBudget Absent { get; } = new(StatedBudgetState.Absent, 0, string.Empty);

    /// <summary>A taken budget of <paramref name="seconds"/>, which must be positive.</summary>
    public static StatedBudget Of(int seconds) =>
        seconds > 0
            ? new(StatedBudgetState.Stated, seconds, string.Empty)
            : throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "a stated budget is a positive number of seconds");

    /// <summary>A value that was sent and not taken, with the reason its note gave.</summary>
    public static StatedBudget DroppedFor(string reason) => new(StatedBudgetState.Dropped, 0, reason);
}
