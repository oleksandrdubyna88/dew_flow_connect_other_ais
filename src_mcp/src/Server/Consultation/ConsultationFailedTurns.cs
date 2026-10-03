using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>Who is consulting: the caller's kind and id, a note when the cap is held in memory, and the call it took.</summary>
/// <param name="CounterNote">Empty ordinarily; a sentence when the call cap is being held in memory.</param>
internal sealed record TurnCaller(string Kind, string Id, string CounterNote)
{
    /// <summary>The call this turn took from the cap — what a vendor's failure hands back.</summary>
    public CounterTake Take { get; init; } = CounterTake.Nothing;
}

/// <summary>The consultant a turn runs on: its adapter, the vendor row, the model already materialised, and the caller.</summary>
internal sealed record TurnConsultant(IConsultantRuntime Runtime, ProviderSettings Row, string Model, TurnCaller Caller)
{
    /// <summary>The turn's one refund: whichever failure path claims it first, and none after.</summary>
    public SingleUseTake Refund { get; } = new(Caller.Take);

    /// <summary>What THIS turn consumed — <see cref="ConsultationBilling.ThisTurnsShare"/>, for this consultant's reporting.</summary>
    public Usage ShareOf(ConsultationRecord record, Usage reported) =>
        ConsultationBilling.ThisTurnsShare(Runtime.UsageIsCumulative, record, reported);
}

/// <summary>
/// A consultation turn that ended without advice, decided and APPLIED: the record, the give-back, the ledger row, the
/// caller kind's health file, and the caller's answer.
/// </summary>
/// <remarks>
/// <para>The IO half beside <see cref="ConsultationFailing"/>, which builds every record transition and sentence as a
/// pure function. Moved out of <see cref="ConsultationService"/> by the whole-branch review of
/// PLAN_the_consultant_works_on_every_vendor.md (2026-10-03, finding G): that file had grown past its merge-base size
/// (1031 lines) to 1070, in a file the plan allowed wiring only.</para>
/// <para><b>Decided once, refunded at most once, after the record is written</b> (epic 2's review): a record write
/// that throws while a failure is applied reaches the turn's catch-all with the failure already decided, and
/// <see cref="AfterTheLaunch"/> applies the SAME one.</para>
/// <para><b>The answer leaves through the service's own refusal helper</b> (<paramref name="refuse"/>), never past
/// it: this type names no refusal boundary, so the census of refusal roads (<c>shared/refusal-sites.json</c>) is
/// unchanged — and it passes the failure KIND as the notice's subject (<c>consult:&lt;kind&gt;</c>), because the
/// compiler-filled member name was the same for every failure and collapsed them into one row (the review, D).</para>
/// </remarks>
/// <param name="refuse">The service's refusal helper: the sentence, and the notice's subject.</param>
internal sealed class ConsultationFailedTurns(
    ConsultationStore store,
    ConsultCallCounter counter,
    ConsultHealthStore health,
    UsageLedger ledger,
    string dataDir,
    Serilog.ILogger log,
    Func<string, string, string> refuse)
{
    /// <summary>
    /// The failure this turn ended in, decided once — or null when the consultant answered: a breach, a launch that
    /// ended on its own failure, or a clean exit with nothing in it, classified by <see cref="ConsultFailures.Classify"/>
    /// with its transcript kept. Writes nothing but that transcript.
    /// </summary>
    public ConsultationFailed? Decided(ConsultationRecord record, TurnConsultant consultant, ConsultantTurnResult turned, IReadOnlyList<TreeChange> changes, ConsultFailure killedAs)
    {
        var facts = Facts(record, consultant, turned.SurvivingHandle, consultant.ShareOf(record, turned.TurnUsage));
        if (changes.Count > 0)
        {
            var breach = new ConsultFailure.TreeChanged(FilesystemSnapshot.Sentence(changes));
            log.Error("{Kind} consultation {Id}: {Alert}", record.Kind, record.Id, breach.Sentence);

            return ConsultationFailing.Breach(facts, breach);
        }

        return ConsultantTurnBooks.Answered(turned.Final)
            ? null
            : Unanswered(consultant, turned.Final, facts, killedAs);
    }

    /// <summary>
    /// Applies a decided failure: the record first, and only once it is written, the give-back — then the ledger row,
    /// the caller kind's health file and the answer.
    /// </summary>
    public string Failing(TurnConsultant consultant, TimeSpan elapsed, ConsultationFailed failed)
    {
        store.Write(failed.Next);

        return Applied(consultant, elapsed, failed, string.Empty);
    }

    /// <summary>
    /// The adapter refused before anything was launched (claude: its <c>--help</c> never answered, or could not be
    /// started). Logged as a warning, given back, billed, written to the health file and answered — and the RECORD is
    /// left as it was.
    /// </summary>
    /// <remarks>
    /// The whole-branch review, E: nothing was launched, so nothing about the CONVERSATION went wrong. Writing the failure
    /// to the record ended an existing consultation two turns deep over a <c>--help</c> that timed out once, and created
    /// a failed record for a new one nothing was ever sent to. An existing record keeps its status; a new one is never
    /// written, and its health file names no consultation id that would lead nowhere.
    /// </remarks>
    public string RefusedBeforeTheLaunch(ConsultationRecord record, TurnConsultant consultant, ConsultFailure failure)
    {
        log.Warning("{Kind} consultation {Id}: not launched — {What}; {Cure}", record.Kind, record.Id, failure.What(consultant.Row.Provider), failure.Cure);
        var failed = ConsultationFailing.BeforeTheLaunch(Facts(record with { Confinement = string.Empty }, consultant, string.Empty, Usage.None), failure);

        return Applied(consultant, TimeSpan.Zero, store.Read(record.Id) is null ? failed with { Next = failed.Next with { Id = string.Empty } } : failed, string.Empty);
    }

    /// <summary>
    /// The turn faulted after the record was marked <c>asking</c> — the deadline fired, a snapshot threw, or the record
    /// write did. Settle the record and answer a sentence; never let it stick at <c>asking</c>, and never throw a
    /// protocol error up the stdio stack.
    /// </summary>
    /// <remarks>
    /// A turn the vendor may already have ACCEPTED stays resumable: the handle is read off whatever the launch captured,
    /// and with one the record becomes <c>interrupted</c> and the turn is not counted. The write is best-effort because
    /// the fault may itself be that the record cannot be written — but a record left <c>asking</c> is swept once its
    /// deadline passes and its lock is free, so it can no longer stick for ever. (2026-09-26.)
    /// </remarks>
    /// <param name="failed">The failure the turn had already decided, or the one the fault itself is.</param>
    /// <param name="unrecorded">Why a decided failure's record could not be written; empty when nothing was decided before the fault.</param>
    public string AfterTheLaunch(TurnConsultant consultant, TimeSpan elapsed, ConsultationFailed failed, string unrecorded)
    {
        Quietly(() => store.Write(failed.Next));

        return Applied(consultant, elapsed, failed, Core.Notices.Redaction.SafeText(unrecorded, Core.Notices.Redaction.TitleLimit));
    }

    /// <summary>The failure a fault IS when the turn had decided none (<see cref="ConsultationFailing.FaultIs"/>).</summary>
    public static ConsultationFailed Undecided(ConsultationRecord asking, TurnConsultant consultant, IReadOnlyList<ReviewerLaunch> launched, Exception fault, ConsultFailure killedAs) =>
        ConsultationFailing.AfterTheLaunch(
            Facts(asking, consultant, ConsultantTurn.SurvivingHandle(consultant.Runtime, launched), consultant.ShareOf(asking, ConsultantTurn.UsageOf(consultant.Runtime, launched))),
            ConsultationFailing.FaultIs(killedAs, fault));

    /// <summary>The turn's ledger row (<see cref="ConsultantTurnBooks.Billed"/>), role <c>consult</c>.</summary>
    public void Billed(TurnConsultant consultant, string outcome, TimeSpan elapsed, Usage usage) =>
        ConsultantTurnBooks.Billed(ledger, consultant.Row, consultant.Model, ConsultantRoles.Consult, outcome, elapsed, usage);

    /// <summary>A best-effort write on a path that is already failing — it must not mask what failed.</summary>
    public void Quietly(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning("consultations: the record could not be updated on the failure path: {Reason}", e.Message);
        }
    }

    public static TurnFailure Facts(ConsultationRecord record, TurnConsultant consultant, string turnsHandle, Usage share) =>
        new(record, consultant.Row.Provider, turnsHandle, share, ConsultationStore.Stamp(DateTime.UtcNow));

    private ConsultationFailed Unanswered(TurnConsultant consultant, ReviewerLaunch final, TurnFailure facts, ConsultFailure killedAs)
    {
        var failure = Kept(consultant, final, ConsultFailures.Classify(consultant.Runtime, final, facts.Record.Runtime, killedAs));

        return final.Terminal is { } terminal
            ? ConsultationFailing.Terminal(facts, failure, ReviewerSummaryFactory.Describe(terminal))
            : ConsultationFailing.Silent(facts, failure);
    }

    /// <summary>The give-back, the ledger, the health file and the answer — everything after the record.</summary>
    private string Applied(TurnConsultant consultant, TimeSpan elapsed, ConsultationFailed failed, string unrecorded)
    {
        var gaveBack = GaveBack(consultant, failed.Failure);
        Billed(consultant, failed.LedgerOutcome, elapsed, failed.Share);
        health.Failed(ConsultHealth.FailureOf(failed));

        return refuse(ConsultationFailing.Answer(failed, gaveBack, unrecorded), $"consult:{failed.Failure.Kind}");
    }

    /// <summary>
    /// Hands the caller's call back when the failure was not its doing — a STUCK turn's take only, at most once per turn
    /// (<see cref="SingleUseTake"/>), bounded and fenced by the counter itself.
    /// </summary>
    private bool GaveBack(TurnConsultant consultant, ConsultFailure failure) =>
        failure.GivesTheCallBack && counter.GiveBack(consultant.Refund.Claim(), DateTime.UtcNow);

    /// <summary>The failure, with where its launch's transcript was kept — when there was one to keep.</summary>
    /// <remarks>
    /// Kept by <see cref="ConsultantTurnBooks.Kept"/> — redacted first, bounded, atomic, in <c>unparseable/consultations/</c>,
    /// a directory of its own whose retention must not reach the reviewers' evidence (epic 2's review).
    /// </remarks>
    private ConsultFailure Kept(TurnConsultant consultant, ReviewerLaunch final, ConsultFailure failure) =>
        ConsultantTurnBooks.Kept(dataDir, Core.Rounds.FileName.Safe(consultant.Row.Provider), final, failure,
            e => log.Warning("evidence: could not keep the consultant's transcript: {Reason}", e.Message));
}
