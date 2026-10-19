namespace CoaiMcp.Store;

/// <summary>
/// The caller's own transcript, read text-free: what its Claude session and every subagent it launched did — messages, models, effort,
/// tokens, launches, parent links, writes — counted exactly once and keyed by the session id, never by <c>cwd</c> (E3.S4 of
/// <c>PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>; the read algorithm and its decisions are there).
/// </summary>
/// <remarks>
/// <para>It reads <c>&lt;session&gt;.jsonl</c>, every <c>subagents/agent-&lt;id&gt;.jsonl</c> and every <c>.meta.json</c>, through
/// <see cref="TranscriptLines.ReadSharedFrom"/> (a line before the bound is skipped on its raw bytes, never decoded) and one
/// <see cref="ReadMeter"/>; <see cref="TranscriptLine"/> decides what is taken from a line, <see cref="TranscriptShapes"/> what may be
/// kept. It says which of four states it reached — observed (also with zero subagents), partial, unobserved — and why, in
/// <see cref="ObservationReasons"/>: nothing read from a transcript can become a reason. Nothing here is persisted; E5 stores, E4 attributes.</para>
/// <para><b>Never reads</b> <c>cwd</c>, a prompt, a description, a message's text or a <c>toolUseResult</c>, and never opens
/// <c>tool-results/</c>.</para>
/// </remarks>
public static class CallerTranscript
{
    /// <summary>
    /// A message's lines are not adjacent: the lines of one id span up to 2.01 h (E1, Q4), so the gate reads this long BEFORE the bound
    /// (3 h = 1.5 × the measured maximum), and a budget stop treats an id seen this recently as possibly unfinished.
    /// </summary>
    public static readonly TimeSpan StraddleGuard = TimeSpan.FromHours(3);

    /// <summary>
    /// S4-1: a torn LAST line is a tail, counted in <c>Diagnostics.TornTails</c> and NOT partial. Flip this — with a
    /// <see cref="ObservedSession.ReaderVersion"/> bump — only if E1's measurement shows tails that are NOT transient: it saw 0 torn tails in 9 379
    /// live samples, so a tail is the CLI's append racing the read, and by the evidence cutoff its bytes belong to a line written after the
    /// round being attributed. Not a <c>const</c>: a constant condition is unreachable code to the compiler.
    /// </summary>
    public static readonly bool TornTailCountsAsPartial = false;

    /// <summary>
    /// Reads the located session. <paramref name="fromUtc"/> is a lower bound on a MESSAGE's earliest line (null reads everything);
    /// <paramref name="budget"/> limits what the read may spend. A location that is not observable is carried through as
    /// <c>unobserved</c> and NOTHING is opened.
    /// </summary>
    public static ObservedSession Read(CallerTranscriptLocation where, ReadBudget budget, DateTime? fromUtc) => where switch
    {
        CallerTranscriptLocation.Found found => new TranscriptRead(found, budget, fromUtc).Run(),
        CallerTranscriptLocation.NotObservable refused => Unobserved(refused),
        _ => throw new ArgumentException("A transcript location is Found or NotObservable.", nameof(where)),
    };

    /// <summary>A date minus a span, floored at <see cref="DateTime.MinValue"/> instead of throwing.</summary>
    internal static DateTime Before(DateTime at, TimeSpan span) => at <= DateTime.MinValue + span ? DateTime.MinValue : at - span;

    private static ObservedSession Unobserved(CallerTranscriptLocation.NotObservable refused) =>
        new(refused.Session, ObservationState.Unobserved(refused.Reason), null, [], [], [], [], ReadDiagnostics.None);
}
