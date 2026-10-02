using System.Globalization;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>Where the work stands, as the server INFERRED it (D7) — never as the caller declared it.</summary>
public enum AskPhase
{
    /// <summary>No plan round has reached <c>proceed</c> — or there is no session at all. The person's door.</summary>
    Planning,

    /// <summary>The plan has proceeded and the work is not yet released to the stage environment. The gate's door.</summary>
    Building,

    /// <summary>Every epic closed, or the session done after its code round. The person's door again.</summary>
    Released,
}

/// <summary>What the <c>consultId</c> a caller passed came to once VERIFIED (D14 (a)) — a closed union.</summary>
public abstract record ConsultProof
{
    /// <summary>No id was passed.</summary>
    public sealed record None : ConsultProof;

    /// <summary>A terminal consultation this caller asked in this repository, young enough and unused.</summary>
    /// <param name="Note">Empty when the consultants answered; the stand-down sentence when the record says none could be had (D9).</param>
    public sealed record Verified(string ConsultId, string Note) : ConsultProof;

    /// <summary>An id that does not count — refused as if none were given, and the caller is told why.</summary>
    public sealed record Rejected(string ConsultId, string Why) : ConsultProof;

    private ConsultProof() { }

    public static ConsultProof Nothing { get; } = new None();
}

/// <summary>Everything the gate decides on, gathered by the server before the decision (<see cref="AskGate.Decide"/>).</summary>
/// <remarks>Defaults are the ordinary call in the building phase under the shipped mode, so a table test names only what it varies.</remarks>
public sealed record AskGateInput
{
    public QuestionMode Mode { get; init; } = QuestionMode.Require;

    public AskPhase Phase { get; init; } = AskPhase.Building;

    /// <summary>How many batches of questions the person has been asked since the plan's proceed.</summary>
    public int BatchesAsked { get; init; }

    public int FreeBatches { get; init; } = QuestionPolicy.FreeBatches;

    /// <summary>The caller's declared bypass (A8): the person at once, the consultants beside.</summary>
    public bool ProductionRisk { get; init; }

    public string RiskReason { get; init; } = string.Empty;

    public ConsultProof Proof { get; init; } = ConsultProof.Nothing;

    /// <summary>Empty when a consultant can be had right now; else why not (switched off, no row on, the quota) — D9's stand-down.</summary>
    public string Unavailable { get; init; } = string.Empty;

    /// <summary>The phase record exists and could not be read: the gate lets the question through and says so.</summary>
    public bool PhaseUnreadable { get; init; }
}

/// <summary>What the gate decided — a closed union.</summary>
public abstract record AskDecision
{
    /// <param name="Note">What the reply says beside the person's answer; empty when there is nothing to say.</param>
    /// <param name="Counted">Whether this question is a batch the phase store records once the card is written.</param>
    /// <param name="ConsultBeside">D8: the consultants run in the background and their answers are folded under the card.</param>
    public sealed record Allowed(string Note, bool Counted, bool ConsultBeside) : AskDecision;

    public sealed record Refused(string Sentence) : AskDecision;

    private AskDecision() { }
}

/// <summary>
/// The gate behind <c>ask_human</c> (<c>todo/PLAN_question_consultant.md</c> D7–D9, D14 (a)): pure over
/// <see cref="AskGateInput"/>, so the whole table is a test.
/// </summary>
/// <remarks>
/// <para>The table, in the order it is asked: a production risk with no reason is refused in every mode; <c>off</c>,
/// the plan stage and the release let the question through silently; an unreadable phase record lets it through
/// and says so; then, building — a production risk asks the person at once with the consultants beside; a verified
/// consultId opens the door; the free batches go to the person, counted; and past them the gate refuses in
/// <c>require</c>, reminds in <c>remind</c>, and stands down with a note whenever no consultant can be had (D9 —
/// never a deadlock).</para>
/// <para>Erring toward the consultant is the safe direction for an AI that would otherwise interrupt (D7), which is
/// why unknown is not released, and why a rejected consultId counts as none rather than as a reason to let the
/// question through.</para>
/// </remarks>
public static class AskGate
{
    public static AskDecision Decide(AskGateInput input)
    {
        if (input.ProductionRisk && string.IsNullOrWhiteSpace(input.RiskReason))
        {
            return new AskDecision.Refused(
                "productionRisk needs a riskReason — say in one sentence what a wrong answer could do to production. "
                + "Without it the question is an ordinary one, and goes through the question consultant like any other in its phase");
        }

        if (input.Mode == QuestionMode.Off || input.Phase != AskPhase.Building)
        {
            return new AskDecision.Allowed(string.Empty, Counted: false, ConsultBeside: false);
        }

        return input.PhaseUnreadable
            ? new AskDecision.Allowed(
                "the question-phase record could not be read, so the gate let this question through to the person and left the record as it is",
                Counted: false, ConsultBeside: false)
            : Building(input);
    }

    private static AskDecision Building(AskGateInput input)
    {
        if (input.ProductionRisk)
        {
            return Risk(input);
        }

        if (input.Proof is ConsultProof.Verified verified)
        {
            return new AskDecision.Allowed(verified.Note, Counted: true, ConsultBeside: false);
        }

        return input.BatchesAsked < input.FreeBatches
            ? new AskDecision.Allowed(FreeBatch(input), Counted: true, ConsultBeside: false)
            : Required(input);
    }

    /// <summary>Past the free batches: refused, reminded, or stood down — whichever the mode and the availability say.</summary>
    private static AskDecision Required(AskGateInput input)
    {
        if (input.Unavailable.Length > 0)
        {
            return new AskDecision.Allowed(
                $"the question consultant is required here and stood down: {input.Unavailable} — the person is asked",
                Counted: true, ConsultBeside: false);
        }

        return input.Mode == QuestionMode.Require
            ? new AskDecision.Refused(Refusal(input))
            : new AskDecision.Allowed(Reminder(input), Counted: true, ConsultBeside: false);
    }

    private static AskDecision Risk(AskGateInput input) =>
        input.Unavailable.Length > 0
            ? new AskDecision.Allowed(
                $"production risk declared: the person is asked at once; no consultant ran beside the question ({input.Unavailable}){Rejected(input)}",
                Counted: true, ConsultBeside: false)
            : new AskDecision.Allowed(
                $"production risk declared: the person is asked at once; the consultants run beside your question and their answers are folded under its card{Rejected(input)}",
                Counted: true, ConsultBeside: true);

    private static string FreeBatch(AskGateInput input) =>
        $"free batch {Number(input.BatchesAsked + 1)} of {Number(input.FreeBatches)}: from the next one on, until the work is released to the stage "
        + $"environment, ask_consultants comes before ask_human{Rejected(input)}";

    private static string Refusal(AskGateInput input) =>
        $"this question goes to the question consultant first: the plan has proceeded, the person has already been asked "
        + $"{Number(input.BatchesAsked)} batch(es) of questions since (the free ones are {Number(input.FreeBatches)}), and the work is not yet "
        + "released to the stage environment. Call ask_consultants with the question and the context (what you tried, the "
        + "options), verify every answer, and call ask_human again with the consultId it returned only if the answers do not "
        + "settle it. A question whose wrong answer could take production down passes productionRisk: true with a riskReason "
        + $"instead — the person is asked at once and the consultants run beside.{Rejected(input)}";

    private static string Reminder(AskGateInput input) =>
        $"reminder — the question consultant comes first from here: the person has been asked {Number(input.BatchesAsked)} batch(es) "
        + $"since the plan proceeded (the free ones are {Number(input.FreeBatches)}); call ask_consultants before ask_human until the work "
        + $"is released to the stage environment. This gate reminds; it does not refuse.{Rejected(input)}";

    /// <summary>D14 (a): a consultId that did not count is named, with why, wherever the decision is said.</summary>
    private static string Rejected(AskGateInput input) =>
        input.Proof is ConsultProof.Rejected rejected
            ? $" The consultId you passed ({rejected.ConsultId}) did not count: {rejected.Why}."
            : string.Empty;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
