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

    /// <summary>
    /// What this turn's launch RAN WITH, as its ledger line says it (<see cref="RowLaunchOptions.Answering"/>, then the release its launches
    /// asked) — <see cref="LaunchOptions.None"/> until the launch is built, which is what a turn refused before it bills.
    /// </summary>
    public LaunchOptions RanWith { get; init; } = LaunchOptions.None;

    /// <summary>What THIS turn consumed and the vendor's counter after it — <see cref="ConsultationBilling.ThisTurnsBill"/>, for this consultant's reporting.</summary>
    public CumulativeShare BillOf(ConsultationRecord record, Usage reported) =>
        ConsultationBilling.ThisTurnsBill(Runtime.UsageIsCumulative, record, reported, Runtime.UsageCounterMayRestart);

    /// <summary>What the caller's answer says this turn cost: its SHARE (<see cref="BillOf"/>), the figure the record and the ledger get.</summary>
    public double? ReplyCost(ConsultationRecord record, ConsultantTurnResult turned) => BillOf(record, turned.TurnUsage).Share.CostUsd;
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
    /// <param name="statusBefore">The record's status before this turn marked it <c>asking</c> (<see cref="TurnFailure.StatusBefore"/>).</param>
    public ConsultationFailed? Decided(ConsultationRecord record, TurnConsultant consultant, ConsultantTurnResult turned, IReadOnlyList<TreeChange> changes, ConsultFailure killedAs, string statusBefore = "")
    {
        var facts = Facts(record, consultant, turned.SurvivingHandle, consultant.BillOf(record, turned.TurnUsage)) with { StatusBefore = statusBefore };
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

    /// <summary>
    /// The failure a fault IS when the turn had decided none (<see cref="ConsultationFailing.FaultIs"/>) — and, when the turn's deadline struck
    /// before ANY launch landed (for Kimi: the wait for the sign-in's gate), <see cref="ConsultationFailing.NothingLaunched"/>, naming what the adapter's
    /// launch control waited on (<see cref="IConsultantRuntime.LaunchWait"/>; E5.4 deviation 32).
    /// </summary>
    /// <param name="statusBefore">The record's status before this turn marked it <c>asking</c> (<see cref="TurnFailure.StatusBefore"/>).</param>
    public static ConsultationFailed Undecided(ConsultationRecord asking, TurnConsultant consultant, IReadOnlyList<ReviewerLaunch> launched, Exception fault, ConsultFailure killedAs, string statusBefore = "")
    {
        var facts = Facts(asking, consultant, ConsultantTurn.SurvivingHandle(consultant.Runtime, launched), consultant.BillOf(asking, ConsultantTurn.UsageOf(consultant.Runtime, launched)))
            with
        { StatusBefore = statusBefore };

        return ConsultationFailing.FaultIs(killedAs, fault) switch
        {
            ConsultFailure.Deadline deadline when launched.Count == 0 => ConsultationFailing.NothingLaunched(facts, deadline with { WaitedOn = consultant.Runtime.LaunchWait }),
            var failure => ConsultationFailing.AfterTheLaunch(facts, failure),
        };
    }

    /// <summary>The turn's ledger row (<see cref="ConsultantTurnBooks.Billed"/>), role <c>consult</c>.</summary>
    public void Billed(TurnConsultant consultant, string outcome, TimeSpan elapsed, Usage usage, string consultation, string resetsUtc = "") =>
        ConsultantTurnBooks.Billed(ledger, consultant.Row, consultant.Model, ConsultantRoles.Consult, outcome, elapsed, usage, consultant.RanWith, consultation: consultation, resetsUtc: resetsUtc);

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

    /// <summary>The facts of a turn whose launches reported: its share, and the vendor's counter after it.</summary>
    public static TurnFailure Facts(ConsultationRecord record, TurnConsultant consultant, string turnsHandle, CumulativeShare bill) =>
        Facts(record, consultant, turnsHandle, bill.Share) with { Counter = bill.Counter };

    private ConsultationFailed Unanswered(TurnConsultant consultant, ReviewerLaunch final, TurnFailure facts, ConsultFailure killedAs)
    {
        var failure = Kept(consultant, final, ConsultFailures.Classify(consultant.Runtime, final, facts.Record.Runtime, killedAs));

        // The ledger's outcome: the vendor's reason whole and redacted, never the summary's unredacted 160-character
        // cut (story 3.3 of research/PLAN_every_round_is_counted.md).
        // And the reset the refusal stated, for the same row (epic 5's code round of research/PLAN_every_round_is_counted.md).
        return final.Terminal is { } terminal
            ? ConsultationFailing.Terminal(facts, failure, ReviewerSummaryFactory.DescribeWhole(terminal)) with
            {
                ResetsUtc = ConsultantTurnBooks.StatedResetOf(final),
            }
            : ConsultationFailing.Silent(facts, failure);
    }

    /// <summary>The give-back, the ledger, the health file and the answer — everything after the record.</summary>
    private string Applied(TurnConsultant consultant, TimeSpan elapsed, ConsultationFailed failed, string unrecorded)
    {
        var gaveBack = GaveBack(consultant, failed.Failure);
        // The record's id — emptied by RefusedBeforeTheLaunch when no record was ever written, so such a line names no consultation.
        Billed(consultant, failed.LedgerOutcome, elapsed, failed.Share, failed.Next.Id, failed.ResetsUtc);
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
