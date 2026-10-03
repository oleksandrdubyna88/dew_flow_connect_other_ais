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

    /// <summary>The caller's declared bypass (A8): the consultants beside a held gate's card, or first for the AI's own question (G3).</summary>
    public bool ProductionRisk { get; init; }

    public string RiskReason { get; init; } = string.Empty;

    public ConsultProof Proof { get; init; } = ConsultProof.Nothing;

    /// <summary>Empty when a consultant can be had right now; else why not (switched off, no row on, the quota) — D9's stand-down.</summary>
    public string Unavailable { get; init; } = string.Empty;

    /// <summary>The phase record exists and could not be read: the gate lets the question through and says so.</summary>
    public bool PhaseUnreadable { get; init; }

    /// <summary>
    /// Where the person is asked — decided by the server from the session BEFORE the gate (<c>todo/PLAN_ask_human_is_for_the_gate.md</c>,
    /// G1): a held gate's question is a card, every other question is the AI's own and is asked in its conversation.
    /// The default is the AI's own question, as every other default here is the ordinary call.
    /// </summary>
    public AskDoor Door { get; init; } = AskDoor.Conversation;
}

/// <summary>The two places a question reaches the person (<c>todo/PLAN_ask_human_is_for_the_gate.md</c>).</summary>
public enum AskDoor
{
    /// <summary>A card in VS Code — only for the GATE's question: a held gate, or a feature review that can still be asked for its second round.</summary>
    Card,

    /// <summary>The AI's own question: asked in the AI's own conversation, with its own question tool. No card is written.</summary>
    Conversation,
}

/// <summary>What the gate decided — a closed union.</summary>
public abstract record AskDecision
{
    /// <param name="Note">What the reply says beside the person's answer; empty when there is nothing to say.</param>
    /// <param name="Counted">Whether this question is a batch the phase store records once the card is written.</param>
    /// <param name="ConsultBeside">D8: the consultants are asked about a production risk — beside the card, their answers folded
    /// under it; or, in the conversation, FIRST, their answers returned with the reply (G3).</param>
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
/// and says so; then, building — a production risk asks the consultants (beside a held gate's card, or FIRST for the AI's
/// own question, <c>todo/PLAN_ask_human_is_for_the_gate.md</c> G3); a verified
/// consultId opens the door; the free batches go to the person, counted; and past them the gate refuses in
/// <c>require</c>, reminds in <c>remind</c>, and stands down with a note whenever no consultant can be had (D9 —
/// never a deadlock).</para>
/// <para>Erring toward the consultant is the safe direction for an AI that would otherwise interrupt (D7), which is
/// why unknown is not released, and why a rejected consultId counts as none rather than as a reason to let the
/// question through.</para>
/// <para><b>A held gate's question is not the AI's</b> (<c>todo/PLAN_ask_human_is_for_the_gate.md</c>, G7): on the
/// card door the phase rule does not apply — the gate is asking the person, the consultants cannot release a hold,
/// and the batches count the AI's own questions. Only a production risk keeps D8 there.</para>
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

        return AsksNoConsultant(input) ? new AskDecision.Allowed(string.Empty, Counted: false, ConsultBeside: false) : Building(input);
    }

    /// <summary>
    /// The building phase: a production risk first — it needs no phase count, so an unreadable phase record does not
    /// cost it its consultants (the own review of 2026-10-03) — then the unreadable record, then the batches.
    /// </summary>
    private static AskDecision Building(AskGateInput input) =>
        input.ProductionRisk ? Risk(input)
        : input.PhaseUnreadable ? new AskDecision.Allowed(
            "the question-phase record could not be read, so the gate let this question through to the person and left the record as it is",
            Counted: false, ConsultBeside: false)
        : Batches(input);

    /// <summary>A question the AI puts in the building phase, judged by the proof and the batches the person has been asked.</summary>
    private static AskDecision Batches(AskGateInput input)
    {
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

    /// <summary>
    /// Off, the plan stage, the release — and a held gate's own question, which is the gate asking rather than the AI
    /// (G7). A production risk on a held gate still runs its consultants beside the card (D8).
    /// </summary>
    private static bool AsksNoConsultant(AskGateInput input) =>
        input.Mode == QuestionMode.Off
        || input.Phase != AskPhase.Building
        || (input.Door == AskDoor.Card && !input.ProductionRisk);

    /// <summary>
    /// D8 on a held gate's card — the person at once, the consultants beside, not one of the AI's batches; G3 in the
    /// conversation — the consultants FIRST, their answers in the reply, counted like any question the AI puts.
    /// </summary>
    private static AskDecision Risk(AskGateInput input) =>
        input.Unavailable.Length > 0
            ? new AskDecision.Allowed(
                $"production risk declared: the person is asked at once; no consultant could be asked ({input.Unavailable}){Rejected(input)}",
                Counted: input.Door == AskDoor.Conversation, ConsultBeside: false)
            : new AskDecision.Allowed(RiskNote(input.Door) + Rejected(input), Counted: input.Door == AskDoor.Conversation, ConsultBeside: true);

    private static string RiskNote(AskDoor door) => door == AskDoor.Card
        ? "production risk declared: the person is asked at once; the consultants run beside your question and their answers are folded under its card"
        : "production risk declared: the consultants were asked first and their answers come with this reply — show them to the person beside your question";

    private static string FreeBatch(AskGateInput input) =>
        $"free batch {Number(input.BatchesAsked + 1)} of {Number(input.FreeBatches)}: from the next one on, until the work is released to the stage "
        + $"environment, ask_consultants comes before ask_human{Rejected(input)}";

    private static string Refusal(AskGateInput input) =>
        $"this question goes to the question consultant first: the plan has proceeded, the person has already been asked "
        + $"{Number(input.BatchesAsked)} batch(es) of questions since (the free ones are {Number(input.FreeBatches)}), and the work is not yet "
        + "released to the stage environment. Call ask_consultants with the question and the context (what you tried, the "
        + "options), verify every answer, and call ask_human again with the consultId it returned only if the answers do not "
        + "settle it. A question whose wrong answer could take production down passes productionRisk: true with a riskReason "
        + $"instead — ask_human then asks the consultants first and returns their answers with its reply.{Rejected(input)}";

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
