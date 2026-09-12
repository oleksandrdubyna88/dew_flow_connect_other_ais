namespace CoaiMcp.Core.Consultation;

/// <summary>
/// Where one consultation stands against its cap — and the sentence the consultant is told about it.
/// </summary>
/// <remarks>
/// <para>Computed on the SERVER only, once per turn, so there is exactly one arithmetic: the panel
/// shows the cap, the prompt states the position, and the two cannot disagree about turn 3 of 5.</para>
/// <para>Two arms rather than a number. A consultant told "0 remain" and nothing else still asks a
/// clarifying question — measured behaviour of every CLI model tried — so the LAST turn says in words
/// what a zero means: answer now. (The clean architecture pass, 2026-09-12.)</para>
/// </remarks>
/// <param name="Cap">How many turns this consultation may take. Frozen on the record at creation.</param>
/// <param name="Spent">Turns already answered. An interrupted turn is not counted.</param>
public readonly record struct TurnBudget(int Cap, int Spent)
{
    /// <summary>The turn about to run, 1-based.</summary>
    public int Turn => Spent + 1;

    /// <summary>Turns left AFTER this one. Never negative.</summary>
    public int Remaining => Math.Max(Cap - Turn, 0);

    /// <summary>This turn is the final one the consultant will get.</summary>
    public bool IsLast => Turn >= Cap;

    /// <summary>Every turn has been spent; no further turn may start.</summary>
    public bool Exhausted => Spent >= Cap;

    /// <summary>The sentence that goes into the consultant's prompt for this turn.</summary>
    public string Line() => IsLast
        ? $"This is turn {Turn} of {Cap} and it is the LAST. Answer now — do not ask a clarifying "
          + "question; if you are uncertain, say what you would check and give your best answer anyway."
        : $"You have {Cap} turns in this consultation; this is turn {Turn}, {Remaining} remain. "
          + "Spend a turn on a clarifying question only if you genuinely cannot answer without it.";
}
