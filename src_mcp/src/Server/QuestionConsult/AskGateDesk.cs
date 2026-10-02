using System.Globalization;
using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Server;

/// <summary>The three gate arguments of one <c>ask_human</c> call, as the caller passed them.</summary>
public sealed record AskArguments(string ConsultId, bool ProductionRisk, string RiskReason);

/// <summary>Everything one call's gate decided on, and what the desk needs afterwards to record what happened.</summary>
/// <param name="Input">What the gate decides on.</param>
/// <param name="Key">Which phase record this question counts into.</param>
/// <param name="Released">The inference, kept so the record's transition is the one the gate saw.</param>
/// <param name="Caller">The caller session, as the consultation records name it.</param>
public sealed record AskFacts(AskGateInput Input, QuestionPhaseKey Key, bool Released, string Caller);

/// <summary>
/// The server's half of the gate (<c>todo/PLAN_question_consultant.md</c>, S3): gathers the facts
/// <see cref="AskGate"/> decides on — the phase from the session and the cadence record, the count from the phase
/// store, the consultId VERIFIED (D14 (a)), whether a consultant can be had (D9) — and, once the card is written,
/// records the batch, spends the proof, and runs the consultants beside a production risk (D8).
/// </summary>
/// <remarks>
/// <para>Out of <see cref="AskHumanService"/> for the reason the cadence desk is out of <c>PanelService</c>: the
/// service asks for a decision and acts on it; every fact and every record is here.</para>
/// <para><b>The beside run is detached and observed.</b> The person is never delayed by the consultants on a
/// production risk, so the fan-out runs after the card is written and attaches its answers to the card as it
/// settles; its catch-all logs, as every detached execution's edge must. It runs on no caller token
/// (reliability.md's documented pair): the caller's token ends with the <c>ask_human</c> call, which is exactly
/// when the consultants may still be answering; the rows' own budgets bound them, and the launcher kills a child
/// at its budget.</para>
/// </remarks>
public sealed class AskGateDesk(
    QuestionConsultService questions,
    CadenceStore cadence,
    QuestionPhaseStore phase,
    Escalations escalations,
    Func<string, string?> env,
    Serilog.ILogger log)
{
    /// <summary>What the gate decides on, for this call.</summary>
    public AskFacts Facts(string repoPath, PersistedSession? session, AskArguments args, DateTime nowUtc)
    {
        var caller = ConsultationService.CallerOf(env, repoPath);
        var key = KeyFor(session, caller);
        var released = StageRelease.Of(session, LoadCadence);
        var phaseOf = session is { State.PlanProceeded: true } ? (released ? AskPhase.Released : AskPhase.Building) : AskPhase.Planning;
        // Observed whenever the plan has proceeded, so the release is stamped on the record the moment it is
        // first seen — which is what lets a plan key reused afterwards start over (D14 (b)).
        var read = phaseOf == AskPhase.Planning ? new QuestionPhaseRead(0, true, string.Empty) : phase.Observe(key, released, nowUtc);
        if (!read.Readable)
        {
            log.Warning("ask_human gate: {Why}; the question is let through", read.Why);
        }

        return new AskFacts(
            new AskGateInput
            {
                Mode = QuestionModes.Of(questions.Options.Mode),
                Phase = phaseOf,
                BatchesAsked = read.Batches,
                FreeBatches = questions.Options.FreeBatches,
                ProductionRisk = args.ProductionRisk,
                RiskReason = args.RiskReason,
                Proof = Proof(args.ConsultId, caller, repoPath, nowUtc),
                Unavailable = questions.Preflight(caller, nowUtc),
                PhaseUnreadable = !read.Readable,
            },
            key, released, caller);
    }

    /// <summary>After the card is written: the batch counted, the proof spent, the consultants beside a production risk.</summary>
    public void AfterTheCard(AskFacts facts, EscalationQuestion card, AskDecision.Allowed allowed)
    {
        if (allowed.Counted)
        {
            Counted(facts, card.Id, DateTime.UtcNow);
        }

        if (facts.Input.Proof is ConsultProof.Verified verified)
        {
            Spent(verified.ConsultId, card.Id);
        }

        if (allowed.ConsultBeside)
        {
            _ = RunBesideAsync(card);
        }
    }

    /// <summary>The phase records on the startup sweep and the one-minute beat (§5: 30 days).</summary>
    public int Sweep(DateTime nowUtc) => phase.Sweep(nowUtc);

    // ---------- the facts ----------

    private static QuestionPhaseKey KeyFor(PersistedSession? session, string caller) =>
        session is { Plan.Length: > 0, CadenceRepoId.Length: > 0 }
            ? new QuestionPhaseKey.Plan(session.CadenceRepoId, EpicRef.PlanKey(session.Plan))
            : new QuestionPhaseKey.Caller(caller);

    private CadenceState? LoadCadence(string repoId, string plan)
    {
        try
        {
            return cadence.Load(repoId, plan);
        }
        catch (CadenceStoreException e)
        {
            log.Warning("ask_human gate: the cadence record of {Plan} could not be read ({Why}); the plan is read as not released", plan, e.Message);

            return null;
        }
    }

    /// <summary>D14 (a): the id must name a terminal consultation this caller asked in this repository, young enough and unused.</summary>
    private ConsultProof Proof(string consultId, string caller, string repoPath, DateTime nowUtc)
    {
        var id = (consultId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            return ConsultProof.Nothing;
        }

        if (questions.Store.Read(id) is not { } record || !Ours(record, caller, repoPath))
        {
            return new ConsultProof.Rejected(id, "it is not a question this caller session asked in this repository");
        }

        return Standing(record) is { Length: > 0 } why ? new ConsultProof.Rejected(id, why) : new ConsultProof.Verified(id, NoteFor(record));
    }

    private static bool Ours(QuestionConsultRecord record, string caller, string repoPath) =>
        string.Equals(record.Caller, caller, StringComparison.Ordinal)
        && ConsultationService.SamePath(record.RepoPath, repoPath, DocumentReader.FollowLink);

    /// <summary>Why a record of ours does not count, or empty: still consulting, too old, or already spent.</summary>
    private static string Standing(QuestionConsultRecord record)
    {
        if (!record.IsOver)
        {
            return "its consultants are still answering — wait for the ask_consultants reply";
        }

        if (DateTime.UtcNow - Ended(record) > QuestionPolicy.ConsultProofAge)
        {
            return $"it ended more than {QuestionPolicy.ConsultProofAge.TotalMinutes:0} minutes ago — ask the consultants again";
        }

        return record.EscalationId.Length > 0
            ? "it was already used by an earlier ask_human — one consultation opens the door once"
            : string.Empty;
    }

    /// <summary>The stand-down sentence when the record says no consultant could be had (D9, the coordinator's decision: <c>failed</c> counts like <c>none_available</c>).</summary>
    private static string NoteFor(QuestionConsultRecord record) => record.Outcome switch
    {
        QuestionOutcomes.QuotaSpent => "the question consultant stood down: this caller session's questions are used up",
        QuestionOutcomes.NoneAvailable => "the question consultant stood down: no row could be asked",
        _ => record.Status == QuestionConsultStatuses.Failed ? "the question consultant stood down: no consultant answered" : string.Empty,
    };

    private static DateTime Ended(QuestionConsultRecord record)
    {
        var stamp = record.EndedUtc.Length > 0 ? record.EndedUtc : record.UpdatedUtc.Length > 0 ? record.UpdatedUtc : record.StartedUtc;

        return DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    // ---------- after the card ----------

    private void Counted(AskFacts facts, string escalationId, DateTime nowUtc)
    {
        try
        {
            phase.Count(facts.Key, escalationId, nowUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The question still reaches the person; what is lost is one batch of the count, and the log says so.
            log.Warning(e, "ask_human gate: question {Id} could not be counted under {Key}", escalationId, facts.Key.Spelled);
        }
    }

    /// <summary>Single use (D14 (a)): the record names the card it became, and the log says the person was asked after the consultants.</summary>
    private void Spent(string consultId, string escalationId)
    {
        if (questions.Store.Read(consultId) is not { } record)
        {
            return;
        }

        questions.Store.Write(record with { Outcome = QuestionOutcomes.PersonAsked, EscalationId = escalationId });
    }

    /// <summary>D8: the consultants beside the person's card, their answers folded under it as the rows settle — the detached edge, with its catch-all.</summary>
    private async Task RunBesideAsync(EscalationQuestion card)
    {
        try
        {
            var context = $"The caller declared a production risk: {card.RiskReason}";
            var record = await questions.BesideAsync(card.RepoPath, card.Question, context, card.SessionId, card.Id, CancellationToken.None);
            if (record is null)
            {
                log.Information("question beside escalation {Id}: nothing could be asked", card.Id);

                return;
            }

            var attached = escalations.Attach(card.Id, [.. record.Rows.Select(Advice)]);
            log.Information("question {Question} beside escalation {Id}: {Rows} row(s) folded under the card: {Attached}", record.Id, card.Id, record.Rows.Count, attached);
        }
        catch (Exception e)
        {
            // The detached edge: nothing above this frame observes the task.
            log.Error(e, "the consultants beside escalation {Id} failed", card.Id);
        }
    }

    private static EscalationAdvice Advice(QuestionRowRecord row) =>
        new(row.RowId, row.Vendor, row.Model, row.PromptTitle, row.Capability, row.Flag, row.Status, row.Reason, row.Advice);
}
