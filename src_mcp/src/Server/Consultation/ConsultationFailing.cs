using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;

namespace CoaiMcp.Server;

/// <summary>Everything a failed turn is written from — the record as it stood, and what the turn left.</summary>
/// <param name="Record">The record as the turn found it (<c>asking</c>).</param>
/// <param name="Vendor">The consultant's vendor id, as the caller is told it.</param>
/// <param name="TurnsHandle">
/// The conversation THIS turn's launches named, or empty. What decides whether the turn is resumable — never
/// the record's older handle, which says only that an EARLIER turn reached the vendor (epic 2's review).
/// </param>
/// <param name="Share">What this turn is billed (<see cref="ConsultationBilling.ThisTurnsShare"/>).</param>
/// <param name="NowUtc">The stamp the record is written with.</param>
internal sealed record TurnFailure(ConsultationRecord Record, string Vendor, string TurnsHandle, Usage Share, string NowUtc)
{
    /// <summary>The handle an ended record keeps: this turn's, else the one it already had — for whoever reads it later.</summary>
    public string KeptHandle => TurnsHandle.Length > 0 ? TurnsHandle : Record.Handle;
}

/// <summary>A failed turn, decided: the record to write, the ledger's outcome word, and whether it can be resumed.</summary>
/// <remarks>
/// The caller's answer is NOT here: it depends on whether the call was given back, and the give-back happens
/// only after the record is written (<see cref="ConsultationFailing.Answer"/>).
/// </remarks>
internal sealed record ConsultationFailed(ConsultationRecord Next, string LedgerOutcome, ConsultFailure Failure, bool Resumable, string Vendor, Usage Share);

/// <summary>
/// The ways a consultation turn ends without advice, as pure builders the service applies.
/// </summary>
/// <remarks>
/// <para>They were four branches inside <c>ConsultationService</c> — the empty answer, the breach, the
/// terminal launch and the fault after the launch — each building its own record, its own ledger word and
/// its own sentence, inline, in a file already past the 800-line ceiling
/// (PLAN_the_consultant_works_on_every_vendor.md, E2.2). Here each is a function of the facts, so every
/// record transition and every sentence is a unit test, and the service does only the IO: write, give back,
/// bill, note the health, answer.</para>
/// <para><b>Resumable is narrow</b> (epic 2's review): a TRANSIENT failure (<see cref="ConsultFailure.Transient"/>)
/// whose conversation THIS turn's launches named. Every other failure ends the consultation, and its record
/// keeps the handle only for whoever reads it later.</para>
/// <para><b>What the caller is told</b> is the same for every failure: what happened, the kind and its cure,
/// where the transcript is, whether the call was given back, and — for every failure no retry cures — that
/// this consultation is not worth asking again (or, where the cure is a reframed ask, that a NEW consult naming the
/// files is the move). The sentence that used to close an empty answer, "try once
/// more with a sharper problem statement", is gone: on 2026-10-02 every empty answer it was attached to was a
/// denied shell command, which no problem statement cures.</para>
/// <para><b>Every failure that is ANSWERED bills</b>: the record's running total advances by the turn's
/// share, so a cumulative vendor's next report is measured against everything already billed. The caller's
/// own cancellation (<see cref="Withdrawn"/>) is the exception: nothing is answered and nothing is billed, as
/// before epic 2.</para>
/// </remarks>
internal static class ConsultationFailing
{
    /// <summary>An answer that came back empty — a denied permission, or nothing the streams explain. Never resumable: there is nothing to pick up.</summary>
    public static ConsultationFailed Silent(TurnFailure turn, ConsultFailure failure) =>
        new(Ended(turn, failure, failure.What(turn.Vendor)), failure.Kind, failure, Resumable: false, turn.Vendor, turn.Share);

    /// <summary>The working tree changed while the consultant ran: the read-only promise was broken.</summary>
    public static ConsultationFailed Breach(TurnFailure turn, ConsultFailure.TreeChanged failure) =>
        new(Ended(turn, failure, "the working tree changed while the consultant was running") with { Alert = failure.Sentence },
            failure.Kind, failure, Resumable: false, turn.Vendor, turn.Share);

    /// <summary>
    /// A launch that ended on a failure of its own. Resumable only on the narrow terms above; a conversation
    /// the vendor no longer holds loses its handle as well.
    /// </summary>
    /// <param name="ledgerOutcome">The executor's own description of the ending — the ledger's word since before epic 2.</param>
    public static ConsultationFailed Terminal(TurnFailure turn, ConsultFailure failure, string ledgerOutcome) =>
        failure is ConsultFailure.ConversationDropped
            ? new(Ended(turn, failure, "the vendor no longer holds this conversation") with { Handle = string.Empty },
                ledgerOutcome, failure, Resumable: false, turn.Vendor, turn.Share)
            : Resolved(turn, failure, ledgerOutcome);

    /// <summary>
    /// The adapter refused before anything was launched — claude whose <c>--help</c> never answered. Nothing ran, so
    /// nothing is resumable and nothing is billed; a <c>vendor-refused</c> failure ends the consultation like any
    /// other non-transient one.
    /// </summary>
    public static ConsultationFailed BeforeTheLaunch(TurnFailure turn, ConsultFailure failure) =>
        Resolved(turn, failure, failure.Kind);

    /// <summary>The turn faulted after the record said <c>asking</c> and before any failure was decided — its deadline, a write, a snapshot.</summary>
    public static ConsultationFailed AfterTheLaunch(TurnFailure turn, ConsultFailure failure) =>
        Resolved(turn, failure, failure.Kind);

    /// <summary>What a KILLED launch was: the caller's token cancelled, the turn's deadline token, or neither — its own timeout.</summary>
    /// <remarks>
    /// Decided by the TOKENS' state, never read off the launch (<c>ProcessResult.Cancelled</c> is set for the
    /// LINKED turn token, so a deadline kill flagged itself as the caller's) and never by an exception's type
    /// (<c>reliability.md</c>) — epic 2's review, the gate's finding 0.
    /// </remarks>
    public static ConsultFailure KilledAs(bool callerCancelled, bool deadlinePassed, TimeSpan deadline) =>
        callerCancelled ? new ConsultFailure.Cancelled()
        : deadlinePassed ? new ConsultFailure.Deadline(deadline)
        : new ConsultFailure.Timeout();

    /// <summary>
    /// The failure a fault IS when the turn had decided none: the caller's cancellation or the turn's deadline
    /// when a token says so, else a write or a snapshot that failed — an <see cref="OperationCanceledException"/>
    /// nobody's token raised is not a deadline.
    /// </summary>
    public static ConsultFailure FaultIs(ConsultFailure killedAs, Exception fault) =>
        killedAs is ConsultFailure.Timeout ? new ConsultFailure.RecordFailed(ConsultFailures.Safe(fault.Message)) : killedAs;

    /// <summary>The caller withdrew the call: the record is settled, and nobody is answered — the cancellation flies on.</summary>
    public static ConsultationRecord Withdrawn(TurnFailure turn)
    {
        var cancelled = new ConsultFailure.Cancelled();

        // Not billed: the share is the caller's to know, and a withdrawn turn was never billed before epic 2.
        return turn.Record with
        {
            Status = ConsultationStatuses.Failed,
            Reason = "the call was cancelled while the consultant was running",
            Handle = turn.KeptHandle,
            FailureKind = cancelled.Kind,
            FailureCure = cancelled.Cure,
            EndedUtc = turn.NowUtc,
            UpdatedUtc = turn.NowUtc,
        };
    }

    /// <summary>
    /// The caller's answer: what happened, then the kind and its cure, then each fact the caller can act on.
    /// </summary>
    /// <param name="gaveBack">Whether the call WAS given back — known only once the record is written.</param>
    /// <param name="unrecorded">Why the record could not be written, when it could not; empty otherwise.</param>
    public static string Answer(ConsultationFailed failed, bool gaveBack, string unrecorded = "") =>
        string.Join(" — ", (string[])
        [
            failed.Resumable ? Resumable(failed) : failed.Failure.What(failed.Vendor),
            $"(failure: {failed.Failure.Kind}) {failed.Failure.Cure}",
            .. Facts(failed.Failure, gaveBack, unrecorded),
        ]);

    private static ConsultationFailed Resolved(TurnFailure turn, ConsultFailure failure, string ledgerOutcome) =>
        failure.Transient && turn.TurnsHandle.Length > 0
            ? new(Interrupted(turn, failure), ledgerOutcome, failure, Resumable: true, turn.Vendor, turn.Share)
            : new(Ended(turn, failure, failure.What(turn.Vendor)), ledgerOutcome, failure, Resumable: false, turn.Vendor, turn.Share);

    private static ConsultationRecord Ended(TurnFailure turn, ConsultFailure failure, string reason) =>
        Classified(turn, failure) with
        {
            Status = ConsultationStatuses.Failed,
            Reason = reason,
            Handle = turn.KeptHandle,
            EndedUtc = turn.NowUtc,
        };

    private static ConsultationRecord Interrupted(TurnFailure turn, ConsultFailure failure) =>
        Classified(turn, failure) with
        {
            Status = ConsultationStatuses.Interrupted,
            Handle = turn.TurnsHandle,
            Reason = failure.What(turn.Vendor),
        };

    /// <summary>The fields every answered failure writes: the classification, the evidence, the bill, the time.</summary>
    private static ConsultationRecord Classified(TurnFailure turn, ConsultFailure failure) =>
        ConsultationBilling.Billing(turn.Record, turn.Share) with
        {
            FailureKind = failure.Kind,
            FailureCure = failure.Cure,
            Evidence = failure.Evidence,
            UpdatedUtc = turn.NowUtc,
        };

    /// <summary>The conversation is kept: the turn is not counted, and the next call picks it up.</summary>
    private static string Resumable(ConsultationFailed failed) =>
        $"the consultant ({failed.Vendor}) accepted the turn but the answer did not arrive: {failed.Failure.What(failed.Vendor)}; "
        + $"the turn was NOT counted — call consult again with consultationId {failed.Next.Id} and the consultant will pick the conversation up";

    /// <summary>The optional sentences, in the order a caller acts on them.</summary>
    private static string[] Facts(ConsultFailure failure, bool gaveBack, string unrecorded) =>
    [
        .. When(failure.Evidence.Length > 0, $"its transcript is kept at {failure.Evidence}"),
        .. When(gaveBack, "this call was not counted against your consult cap"),
        .. When(failure.DoNotRetry, NextMove(failure)),
        .. When(unrecorded.Length > 0, $"and the record could not be updated to say so: {unrecorded}"),
    ];

    private static string[] When(bool holds, string sentence) => holds ? [sentence] : [];

    /// <summary>
    /// What a caller does after a failure no retry of THIS consultation cures: a new, reframed consult when the cure
    /// invites one (the whole-branch review, M — "do not retry" beside "name the files" contradicted itself), else stop.
    /// </summary>
    private static string NextMove(ConsultFailure failure) =>
        failure.InvitesAReframedAsk
            ? "asking this consultation again will not help — a NEW consult whose problem names the files that hold the answer is the right move, or carry on, or ask the person"
            : "do not retry this consultation; carry on, or ask the person";
}
