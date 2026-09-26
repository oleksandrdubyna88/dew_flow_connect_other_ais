using System.Globalization;
using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Server;

/// <summary>What a caller declared about the cadence on one <c>review_plan</c> or <c>review_code</c> call.</summary>
/// <param name="Plan">The plan file, repo-relative. Empty keeps what the session holds.</param>
/// <param name="Epic"><c>k/N</c>. Empty keeps the session's — unless a DIFFERENT plan was named, which clears it.</param>
/// <param name="RiskItems">The answer to the risk question, as JSON: <c>[{"epic":7,"story":"7.2","reason":"…"}]</c>.</param>
/// <param name="RiskNote">Why nothing is risky, when the answer is <c>[]</c>.</param>
public sealed record CadenceArgs(string Plan = "", string Epic = "", string RiskItems = "", string RiskNote = "")
{
    /// <summary>Nothing declared — every call before the cadence existed.</summary>
    public static CadenceArgs None { get; } = new();

    /// <summary>Whether the call carries an answer to the risk question.</summary>
    public bool AnswersRisk => RiskItems.Trim().Length > 0 || RiskNote.Trim().Length > 0;
}

/// <summary>One call's cadence, worked out under the session claim.</summary>
public sealed record CadenceCall
{
    /// <summary>The cadence is off: nothing parsed, nothing refused, nothing ordered.</summary>
    public static CadenceCall Off { get; } = new();

    /// <summary>A refusal the call's own declaration earned — bad shape, too many epics, a count the plan does not have.</summary>
    public string Refusal { get; init; } = string.Empty;

    /// <summary>What the orders are built from.</summary>
    public CadenceFacts Facts { get; init; } = CadenceFacts.Off;

    /// <summary>The plan and epic in force for this call, after the session's were applied; the epic may be empty.</summary>
    public string Plan { get; init; } = string.Empty;

    public string Epic { get; init; } = string.Empty;

    /// <summary>The repository's common dir — what the plan's record and its consultations are keyed by.</summary>
    public string RepoId { get; init; } = string.Empty;

    /// <summary>What the round records about the cadence: met, stood down and why, or a fact about the plan.</summary>
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// The per-call carrier from the stage's <c>RefuseBeforeBuilding</c> — where the cadence is worked out, under
/// the claim, with the resolved sha — to the rest of <c>RunStageAsync</c>, where the orders and the record are.
/// </summary>
/// <remarks>
/// A holder rather than a wider return type: <c>RefuseBeforeBuilding</c> is one delegate shape shared by
/// every stage, and widening it for the one that needs more would touch the document and plan stages for
/// nothing. It lives for exactly one call and is written once.
/// </remarks>
public sealed class CadenceTrace
{
    public CadenceCall Call { get; set; } = CadenceCall.Off;
}

/// <summary>
/// The consultation cadence, as the gate applies it to one call (<c>research/PLAN_consult_on_a_cadence.md</c>, epic 3).
/// </summary>
/// <remarks>
/// <para>Out of <see cref="PanelService"/>, which is past the family's ceiling already: this class holds every
/// cadence decision a round makes, and the service only asks it.</para>
/// <para><b>Mode decides what failure costs.</b> A declaration that is WRONG — a malformed epic, a count the
/// plan does not have, more than fourteen epics, a bad risk answer — is refused in remind and require alike:
/// the caller sent something false and is told so. What the gate could not READ or WRITE — the plan record,
/// the risk answer's write — refuses only in <c>require</c> (fail closed); in <c>remind</c> the round goes ahead
/// with a note, since remind never refuses the work (epic 3's plan and code rounds, codex).</para>
/// <para><b>A plan may be declared without an epic</b> (epic 3's code round, codex): one plan gate for the
/// whole task has no epic yet, and it is where the risk question is naturally answered. The session holds
/// the plan; a code round then needs its epic, which <see cref="BeforeTheCode"/> asks for in require.</para>
/// </remarks>
public sealed class CadenceDesk(
    PanelSettings settings,
    CadenceStore store,
    CadenceGate gate,
    ContextAssembler context,
    GitHistory git,
    Serilog.ILogger log,
    Noticing noticing)
{
    private const string NothingReviewed = " Nothing was reviewed.";

    /// <summary>A desk that only ANSWERS — for the <c>--cadence</c> one-shot the sidebar probes (epic 4 story 4.2).</summary>
    /// <remarks>
    /// <para>Built from the stores and nothing else. A <see cref="PanelService"/> sweeps rounds and consultations,
    /// reprojects the log and sweeps orphan processes in its constructor, and a sidebar that probed through one every
    /// half minute would run that maintenance every time (the risk consultation for story 4.2, point 1).</para>
    /// <para>Its gate is never asked to CHECK — <see cref="AnswerAsync"/> reads the evidence and nothing else — so the
    /// preflight it holds says so rather than probing a consultant. The one write it can make is the one
    /// <c>status</c> makes too: <see cref="Reconciled"/> catching the record up with an epic whose close was missed,
    /// idempotent by construction.</para>
    /// </remarks>
    public static CadenceDesk ForReading(PanelSettings settings, Runners.Processes.IProcessLauncher launcher, Noticing noticing) => new(
        settings,
        new CadenceStore(settings.DataDir),
        new CadenceGate(new ConsultationStore(settings.DataDir), () => new ConsultPreflight(false, "a reader never checks the cadence")),
        new ContextAssembler(launcher),
        new GitHistory(launcher),
        Serilog.Core.Logger.None,
        noticing);

    /// <summary>Where the caller says it is: a plan, and an epic of it or none (Number 0).</summary>
    private sealed record Where(string Plan, string Epic, int Number, int Last)
    {
        public bool HasEpic => Number > 0;
    }

    /// <summary>A plan record as read: the state (a new one when it could not be read) and why not.</summary>
    private sealed record RecordRead(CadenceState State, bool Readable, string Why);

    /// <summary>What storing a risk answer came to: a refusal, a note for the round, or nothing.</summary>
    private sealed record RiskStored(string Refusal, string Note)
    {
        public static RiskStored Nothing { get; } = new(string.Empty, string.Empty);
    }

    /// <summary>Works out this call's cadence: the declaration checked, the plan read at the sha, the record read.</summary>
    public async Task<CadenceCall> PrepareAsync(PersistedSession loaded, CadenceArgs args, string repoPath, string sha, CancellationToken ct)
    {
        if (settings.CadenceMode == CadenceMode.Off)
        {
            return CadenceCall.Off;
        }

        return Declared(loaded, args) switch
        {
            (_, { Length: > 0 } refusal) => Refused(refusal),
            ({ Plan.Length: 0 }, _) => new CadenceCall { Facts = Base(repoPath) },
            (var where, _) => await AtTheShaAsync(loaded, args, repoPath, sha, where, ct),
        };
    }

    /// <summary>The declaration in force, or the reason it cannot be used — nothing read yet.</summary>
    private static (Where Where, string Refusal) Declared(PersistedSession loaded, CadenceArgs args)
    {
        var named = args.Plan.Trim().Replace('\\', '/');
        if (named.Length > 0 && !ConsultAim.IsRepoRelative(named))
        {
            return (NoWhere, $"plan must be the plan file's repo-relative path, e.g. 'todo/PLAN_x.md' — '{named}' is absolute or climbs out of the repository with '..'.");
        }

        var (plan, epic) = Effective(loaded, named, args.Epic.Trim());

        return (plan.Length, epic.Length) switch
        {
            (0, 0) => (NoWhere, args.AnswersRisk ? "riskItems answer a question about ONE plan — send them with plan." : string.Empty),
            (_, 0) => (new Where(plan, string.Empty, 0, 0), string.Empty),
            _ => Parsed(epic, plan),
        };
    }

    private static readonly Where NoWhere = new(string.Empty, string.Empty, 0, 0);

    private static (Where, string) Parsed(string epic, string plan) => EpicRef.Parse(epic, plan) switch
    {
        EpicRef.Some some => (new Where(plan, epic, some.Number, some.Last), string.Empty),
        EpicRef.Refused refused => (NoWhere, refused.Sentence),
        _ => (NoWhere, string.Empty),
    };

    /// <summary>
    /// The plan and epic in force: a non-empty argument wins; an empty one keeps the session's — except that
    /// naming a DIFFERENT plan drops the session's epic, which belonged to the old one (epic 3's plan round).
    /// </summary>
    private static (string Plan, string Epic) Effective(PersistedSession loaded, string named, string epic)
    {
        var plan = named.Length > 0 ? named : loaded.Plan;
        var samePlan = named.Length == 0 || EpicRef.PlanKey(named) == EpicRef.PlanKey(loaded.Plan);

        return (plan, epic.Length > 0 ? epic : samePlan ? loaded.Epic : string.Empty);
    }

    private async Task<CadenceCall> AtTheShaAsync(
        PersistedSession loaded, CadenceArgs args, string repoPath, string sha, Where where, CancellationToken ct)
    {
        var (outline, readNote) = await OutlineAtAsync(repoPath, sha, where.Plan, ct);
        if (AgainstThePlan(where, outline, sha) is { Length: > 0 } wrong)
        {
            return Refused(wrong);
        }

        var repoId = await context.CommonDirAsync(repoPath, ct);
        var risk = StoredRisk(repoId, where.Plan, args);
        if (risk.Refusal.Length > 0)
        {
            return Refused(risk.Refusal);
        }

        var record = Read(loaded, repoId, where.Plan);

        return !record.Readable && settings.CadenceMode == CadenceMode.Require
            ? new CadenceCall { Refusal = CadenceRefusals.Unreadable(record.Why) }
            : Called(repoPath, where, outline, repoId, record, Joined(readNote, risk.Note, Unrecorded(record)));
    }

    /// <summary>What the declaration gets wrong about the plan as it stands at the sha, or empty.</summary>
    private static string AgainstThePlan(Where where, PlanOutline outline, string sha)
    {
        var count = outline.HasEpics ? outline.Epics.Count : where.Last;
        if (CadenceRule.RefuseIfTooMany(count, where.Plan) is { Length: > 0 } tooMany)
        {
            return tooMany;
        }

        return where.HasEpic && outline.HasEpics && (where.Last != outline.LastNumber || outline.Epics.All(e => e.Number != where.Number))
            ? $"epic '{where.Epic}' does not match {where.Plan} at {Short(sha)}: the plan names epics {outline.FirstNumber}-{outline.LastNumber} "
              + $"({count} of them), so declare this epic by its own number and the plan's last, e.g. '{outline.FirstNumber}/{outline.LastNumber}'."
            : string.Empty;
    }

    private CadenceCall Called(string repoPath, Where where, PlanOutline outline, string repoId, RecordRead record, string note) => new()
    {
        Facts = gate.WithEvidence(repoId, Base(repoPath) with
        {
            Plan = where.Plan,
            Epic = where.Number,
            LastEpic = where.HasEpic ? where.Last : outline.LastNumber,
            Outline = outline,
            // A record nobody could read cannot say whether the question was answered; asking again every
            // round of a remind would be noise on top of the note that already says why.
            RiskAnswered = record.State.RiskAnswered || !record.Readable,
            RiskItems = record.State.RiskItems,
        }),
        Plan = where.Plan,
        Epic = where.Epic,
        RepoId = repoId,
        Note = note,
    };

    private string Unrecorded(RecordRead record)
    {
        if (record.Readable)
        {
            return string.Empty;
        }
        log.Warning("cadence: {Why} — the round goes ahead, as remind never refuses", record.Why);

        return $"cadence record unreadable: {record.Why}";
    }

    /// <summary>
    /// The refusal a CODE round earns, if any — and the call with the note the round will carry.
    /// </summary>
    /// <remarks>
    /// Asked only on an epic's FIRST code round in this session: a checkpoint of the same epic, or a retry,
    /// is not asked twice (the epic-1-3 consultation, point 2). Whether a group was consulted is never a
    /// per-session question — the evidence is the consultation records, whatever branch they were taken on.
    /// </remarks>
    public (string Refusal, CadenceCall Call) BeforeTheCode(PersistedSession loaded, CadenceCall call, CommandTexts texts, string branch)
    {
        if (settings.CadenceMode != CadenceMode.Require || call.Refusal.Length > 0)
        {
            return (call.Refusal, call);
        }

        // No epic declared: DeclareTheEpic is empty for a plan that names none, which is no refusal.
        return call.Facts.Epic == 0 ? (DeclareTheEpic(loaded), call) : OnTheFirstCodeRound(loaded, call, texts, branch);
    }

    /// <summary>Asked only on an epic's FIRST code round in this session — a checkpoint of the same epic is not asked twice.</summary>
    private (string, CadenceCall) OnTheFirstCodeRound(PersistedSession loaded, CadenceCall call, CommandTexts texts, string branch) =>
        FirstCodeRound(loaded, call) ? Checked(call, texts, branch) : (string.Empty, call);

    private (string, CadenceCall) Checked(CadenceCall call, CommandTexts texts, string branch)
    {
        switch (gate.Check(call.RepoId, call.Facts, texts))
        {
            case CadenceCheck.Blocked blocked:
                return (blocked.Sentence, call);
            case CadenceCheck.StoodDown stood:
                log.Warning("cadence: a consultation is owed for epic {Epic} of {Plan} and none can be had — {Reason}; the round goes ahead",
                    call.Epic, call.Plan, stood.Reason);
                noticing.Offered(() => StoodDownNotice(call, stood.Reason, branch));
                return (string.Empty, call with { Note = $"stood down: {stood.Reason}" });
            default:
                return (string.Empty, call with { Note = "met" });
        }
    }

    /// <summary>
    /// Records an epic through its code gate when a code stage moves to done — BEFORE the session is saved,
    /// so the common failure (the session write) cannot lose it; a failure here is logged and reconciled
    /// later, never allowed to fail the resolve (the epic-1-3 consultation, point 5; epic 3's plan round).
    /// </summary>
    public void Close(PersistedSession session, string verdict)
    {
        if (Closing(session) is not EpicRef.Some some)
        {
            return;
        }

        try
        {
            store.Update(session.CadenceRepoId, session.Plan, state => state.WithClosed(some.Number, verdict, Stamp()));
        }
        catch (CadenceStoreException e)
        {
            log.Warning("cadence: epic {Epic} of {Plan} passed its code gate but could not be recorded ({Reason}) — "
                + "the next review_code or status for this plan records it", some.Number, session.Plan, e.Message);
        }
    }

    /// <summary>The epic a finished code stage closes — none when the cadence is off or the session holds no plan identity.</summary>
    private EpicRef Closing(PersistedSession session) =>
        settings.CadenceMode == CadenceMode.Off || session.CadenceRepoId.Length == 0
            ? new EpicRef.None()
            : EpicRef.Parse(session.Epic, session.Plan);

    /// <summary>The plan's standing for <c>status</c>, or null when no plan is asked about or held.</summary>
    public async Task<CadenceAnswer?> AnswerAsync(PersistedSession session, string plan, string sha, CancellationToken ct)
    {
        var named = plan.Trim().Length > 0 ? plan.Trim().Replace('\\', '/') : session.Plan;
        if (named.Length == 0 || !ConsultAim.IsRepoRelative(named))
        {
            return null;
        }

        var repoId = await context.CommonDirAsync(session.State.RepoPath, ct);
        var record = Read(session, repoId, named);
        var (outline, _) = await OutlineAtAsync(session.State.RepoPath, sha, named, ct);
        var numbers = Numbers(session, named, outline);
        var known = gate.WithEvidence(repoId, CadenceFacts.Off with { Plan = named });

        return new CadenceAnswer(
            named,
            settings.CadenceMode.ToString().ToLowerInvariant(),
            numbers.Count,
            [.. record.State.Closed.Select(c => c.Number)],
            [.. CadenceRule.GroupsOwed(numbers, settings.CadenceEvery)
                .Select(group => new CadenceGroupAnswer(group.Range, known.Satisfied.Any(done => done.Holds(group.First))))],
            [.. record.State.RiskItems.Select(item => new CadenceRiskAnswer(item.Key, item.Reason, known.SatisfiedRisk.Contains(item.Key)))],
            record.State.RiskAnswered,
            record.Readable ? string.Empty : record.Why);
    }

    // ---------- the pieces ----------

    private CadenceFacts Base(string repoPath) => CadenceFacts.Off with
    {
        Mode = settings.CadenceMode,
        Every = settings.CadenceEvery,
        RiskThreshold = settings.CadenceRiskThreshold,
        RiskMax = settings.CadenceRiskMax,
        RepoPath = repoPath,
    };

    /// <summary>The plan's outline AT THE SHA under review (point 9), or none — with the note saying so.</summary>
    private async Task<(PlanOutline Outline, string Note)> OutlineAtAsync(string repoPath, string sha, string plan, CancellationToken ct)
    {
        var answer = sha.Length > 0 ? await git.FileAtAsync(repoPath, sha, plan, ct) : default;

        return answer.Ran && answer.Ok
            ? (PlanOutlineReader.Of(answer.Out), string.Empty)
            : (PlanOutline.Empty, $"{plan} is not in commit {Short(sha)}, so its number of epics is the caller's word");
    }

    /// <summary>The record, reconciled with the session's last closed epic — or a new state and why it could not be read.</summary>
    private RecordRead Read(PersistedSession session, string repoId, string plan)
    {
        try
        {
            return new RecordRead(Reconciled(session, repoId, plan, store.Load(repoId, plan)), true, string.Empty);
        }
        catch (CadenceStoreException e)
        {
            return new RecordRead(new CadenceState { Plan = plan }, false, e.Message);
        }
    }

    /// <summary>
    /// Re-applies the session's last passed code gate when the record missed it (a write that failed in
    /// <see cref="Close"/>). Idempotent by construction, and catch-up work: a record it cannot write THIS
    /// time is logged and left for the next call, never turned into a refusal (epic 3's code round).
    /// </summary>
    private CadenceState Reconciled(PersistedSession session, string repoId, string plan, CadenceState state)
    {
        var last = session.Rounds.LastOrDefault(r => r.Stage == nameof(Stage.CodeReview));
        if (!Missed(session, last, plan, state))
        {
            return state;
        }

        try
        {
            return store.Update(repoId, plan, current => current.WithClosed(last.EpicNumber, last.Verdict, Stamp()));
        }
        catch (CadenceStoreException e)
        {
            log.Warning("cadence: epic {Epic} of {Plan} is still to be recorded ({Reason}); the next call tries again", last.EpicNumber, plan, e.Message);

            return state.WithClosed(last.EpicNumber, last.Verdict, Stamp());
        }
    }

    /// <summary>Whether the session's last code round closed an epic of this plan that the record does not hold.</summary>
    private static bool Missed(
        PersistedSession session, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] RoundRecord? last, string plan, CadenceState state) =>
        session.State.Stage == Stage.Done && OfThisPlan(last, plan) && !state.IsClosed(last.EpicNumber);

    private static bool OfThisPlan([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] RoundRecord? last, string plan) =>
        last is { EpicNumber: > 0 } && last.PlanKey == EpicRef.PlanKey(plan);

    private static IReadOnlyList<int> Numbers(PersistedSession session, string plan, PlanOutline outline)
    {
        if (outline.HasEpics)
        {
            return [.. outline.Epics.Select(e => e.Number)];
        }

        return EpicRef.PlanKey(plan) == EpicRef.PlanKey(session.Plan) && EpicRef.Parse(session.Epic, session.Plan) is EpicRef.Some some
            ? [.. Enumerable.Range(1, some.Last)]
            : [];
    }

    private static bool FirstCodeRound(PersistedSession loaded, CadenceCall call)
    {
        var key = EpicRef.PlanKey(call.Plan);

        return !loaded.Rounds.Any(round => round.Stage == nameof(Stage.CodeReview) && round.PlanKey == key && round.EpicNumber == call.Facts.Epic);
    }

    /// <summary>
    /// The refusal for a code round of split work that declared no epic — naming the plan's own epics — or
    /// empty for work that was never split into epics.
    /// </summary>
    private string DeclareTheEpic(PersistedSession loaded)
    {
        var outline = PlanOutlineReader.Of(loaded.PlanText);
        if (!IsSplit(outline, loaded.PlanText))
        {
            return string.Empty;
        }

        var named = outline.HasEpics ? $" (it names epics {outline.FirstNumber}-{outline.LastNumber})" : string.Empty;

        return $"this work's plan names epics{named}, and the operator's consultation cadence needs to know which one this "
            + "code round is: call review_code again with plan: \"<the plan file, repo-relative>\" and epic: \"k/N\" — k the "
            + "epic's own number as the plan writes it, N the plan's last." + NothingReviewed;
    }

    /// <summary>Whether this work is split into epics: the plan names two or more, or the split order sized it so.</summary>
    private bool IsSplit(PlanOutline outline, string planText) =>
        outline.Epics.Count >= 2 || (settings.SplitPlan && PlanShapeReader.Of(planText).Verdict >= PlanShape.Split.Medium);

    // ---------- the risk answer ----------

    private RiskStored StoredRisk(string repoId, string plan, CadenceArgs args)
    {
        if (!args.AnswersRisk)
        {
            return RiskStored.Nothing;
        }

        var answer = RiskAnswer.Of(args.RiskItems.Trim().Length == 0 ? "[]" : args.RiskItems, args.RiskNote.Trim(), settings.CadenceRiskMax);
        if (answer is RiskAnswer.Refused refused)
        {
            return new RiskStored(refused.Sentence, string.Empty);
        }

        var given = (RiskAnswer.Given)answer;
        try
        {
            store.Update(repoId, plan, state => state.WithRisk(given.Items, given.Note, Stamp()));

            return RiskStored.Nothing;
        }
        catch (CadenceStoreException e)
        {
            return settings.CadenceMode == CadenceMode.Require
                ? new RiskStored($"the risk answer could not be recorded ({e.Message}) — send it again in a moment.", string.Empty)
                : new RiskStored(string.Empty, $"the risk answer could not be recorded: {e.Message}");
        }
    }

    // ---------- small things ----------

    private static CadenceCall Refused(string sentence) =>
        new() { Refusal = sentence.TrimEnd().EndsWith(NothingReviewed.Trim(), StringComparison.Ordinal) ? sentence : sentence + NothingReviewed };

    private static string Joined(params string[] notes) => string.Join("; ", notes.Where(note => note.Length > 0));

    private static ServerNotice StoodDownNotice(CadenceCall call, string reason, string branch) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = "outcome",
        Source = "cadence",
        Code = ServerNoticeCodes.CadenceStoodDown,
        Subject = CadenceOrders.GroupDue(call.Facts) is { } group ? $"cadence:{group.Range}" : $"cadence:{call.Facts.Epic}",
        Title = ServerNotice.Shortened($"a consultation was owed before epic {call.Epic} of {call.Plan} and none could be had — the round went ahead: {reason}"),
        Branch = branch,
    };

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;

    private static string Stamp() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
}
