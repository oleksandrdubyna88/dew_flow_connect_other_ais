using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>What <c>ask_consultants</c> returns: the question's id, how it ended, every row's answer apart, and what to do next.</summary>
/// <param name="Status"><c>complete</c> · <c>partial</c> · <c>failed</c> · <c>none_available</c> · <c>quota_spent</c> · <c>off</c>.</param>
/// <param name="QuestionsLeft">How many questions this caller session may still ask before the quota sends it to the person.</param>
public sealed record AskConsultantsAnswer(
    string ConsultId,
    string Status,
    IReadOnlyList<QuestionRowAnswer> Answers,
    int QuestionsLeft,
    string Next);

/// <summary>One row's answer — fenced <c>advisory_only</c>, never merged with another's (D2) — or why it has none.</summary>
/// <param name="Flag">D13's caveat on the pair (<c>unconfined</c>, <c>default-deny</c>) or empty — read it beside the advice.</param>
/// <param name="Note">What the row's own record says beside its answer — a disk root the invariant could not watch (S4b), the api row's served sources.</param>
public sealed record QuestionRowAnswer(
    string RowId,
    string Vendor,
    string Model,
    string PromptTitle,
    string Capability,
    string Flag,
    string Status,
    double Seconds,
    double? CostUsd,
    string Reason,
    string Note,
    string Advice);

/// <summary>
/// The <c>ask_consultants</c> tool's whole flow (PLAN_question_consultant.md, S2): the switch, the
/// argument checks, the rows, the per-session quota, the record written before any launch, the fan-out,
/// and every answer fenced and returned separately — with a sentence for every way it can refuse,
/// decided BEFORE anything is launched wherever the fact is ours to know.
/// </summary>
/// <remarks>
/// <para>A collaborator of <see cref="PanelService"/> like <see cref="ConsultationService"/>, and the
/// shape is deliberately the same: the caller is <see cref="ConsultationService.CallerOf"/>, the quota is
/// <see cref="ConsultCallCounter"/> under its own key (<c>q:</c> + caller), the fence is
/// <see cref="ConsultationFence.Advice"/>, the ledger is <c>UsageLedger.RecordJob</c>. What differs is the
/// shape of a question: no conversation, no lock, several rows at once.</para>
/// <para><b>The statuses that are not errors.</b> <c>off</c>, <c>none_available</c> and <c>quota_spent</c>
/// are answers, not refusals: the caller's next move is the same in each — ask the person — and a tool
/// that answered them as errors would make the ordinary road read as a fault. An argument the caller
/// got wrong IS a refusal, naming the cure.</para>
/// </remarks>
public sealed class QuestionConsultService(
    PanelSettings settings,
    IProcessLauncher launcher,
    ReviewerExecutor executor,
    ContextAssembler context,
    RolePrompts prompts,
    UsageLedger ledger,
    VaultKeys keys,
    Serilog.ILogger log,
    Func<string, string?> env,
    Noticing noticing)
{
    /// <summary>The most a question may be: 4 KB (§4). A question is a sentence or a paragraph; past this it is a document.</summary>
    public const int MaxQuestionBytes = 4 * 1024;

    /// <summary>The most context may be: 8 KB (§4) — what was tried and the options, not the code.</summary>
    public const int MaxContextBytes = 8 * 1024;

    /// <summary>The reply's statuses, spelled once.</summary>
    public static class Statuses
    {
        public const string Complete = "complete";
        public const string Partial = "partial";
        public const string Failed = "failed";
        public const string NoneAvailable = "none_available";
        public const string QuotaSpent = "quota_spent";
        public const string Off = "off";
    }

    private readonly QuestionConsultStore _store = new(
        settings.DataDir,
        problem => log.Warning("question consults: {Problem}", problem),
        record => Project(settings, log, record));

    private readonly ConsultCallCounter _counter = new(settings.DataDir);

    private readonly ConsultSchemaFile.Provisioned _answerSchema = Provision(settings, log);

    /// <summary>The projection, as a static so the field initialiser above can reach it (the consultation service's reason).</summary>
    private static void Project(PanelSettings settings, Serilog.ILogger log, QuestionConsultRecord record) =>
        new CoaiMcp.Store.Projection(settings.DataDir, log)
            .Write(db => CoaiMcp.Store.QuestionConsultTable.Record(db, QuestionConsultRows.Head(record), QuestionConsultRows.Entries(record)), "the question");

    private static ConsultSchemaFile.Provisioned Provision(PanelSettings settings, Serilog.ILogger log)
    {
        var provisioned = ConsultSchemaFile.Ensure(
            Path.Combine(settings.DataDir, "schemas"), QuestionAnswerSchema.Name, QuestionAnswerSchema.Json, "An api question row needs it and will be refused");
        if (!provisioned.Ready)
        {
            log.Warning("question consults: {Problem}", provisioned.Problem);
        }

        return provisioned;
    }

    public QuestionConsultStore Store => _store;

    /// <summary>The settings this service was built from — what the gate reads the mode and the free-batch number off.</summary>
    public QuestionConsultSettings Options => settings.QuestionConsult;

    /// <summary>
    /// Whether a consultant can be had right now for this caller, without launching or spending anything: empty
    /// when one can; else why not — the switch, the rows, the quota. What the gate stands down on (D9).
    /// </summary>
    public string Preflight(string caller, DateTime nowUtc)
    {
        var options = settings.QuestionConsult;
        if (!options.Enabled)
        {
            return $"the question consultant is switched off in this installation ({QuestionConsultKeys.Enabled})";
        }

        if (options.RowsUnreadable)
        {
            return $"the question consultant's rows ({QuestionConsultKeys.Rows}) could not be read";
        }

        if (options.Rows.Count(r => r.Enabled) == 0)
        {
            return $"no question-consultant row is switched on ({QuestionAdmission.Section})";
        }

        var peeked = _counter.Peek(QuotaKey(caller), options.QuestionsPerSession, nowUtc);

        return peeked.Allowed ? string.Empty : $"this caller session has asked the consultants {peeked.Used} questions, the cap ({QuestionConsultKeys.QuestionsPerSession} = {options.QuestionsPerSession})";
    }

    public int Sweep(Func<int, bool> isAlive) => _store.Sweep(isAlive, DateTime.UtcNow, settings.QuestionConsult.RowBudget);

    /// <summary>Re-projects every record the store still holds — the consultation service's reason: the projection is allowed to fail, and a terminal record is never written again.</summary>
    public int Reproject()
    {
        var records = _store.All();
        if (records.Count == 0)
        {
            return 0;
        }

        new CoaiMcp.Store.Projection(settings.DataDir, log).Write(
            db =>
            {
                foreach (var record in records)
                {
                    CoaiMcp.Store.QuestionConsultTable.Record(db, QuestionConsultRows.Head(record), QuestionConsultRows.Entries(record));
                }
            },
            "the questions");

        return records.Count;
    }

    /// <param name="sessionOf">Which review session the question is filed under — the service's own resolution of <c>document</c> / <c>feature</c>, handed in.</param>
    public async Task<string> AskAsync(
        string repoPath, string question, string contextText, string document, string feature,
        Func<string, string, string, string, string> sessionOf, CancellationToken ct = default)
    {
        var options = settings.QuestionConsult;
        if (!options.Enabled)
        {
            return Reply(string.Empty, Statuses.Off, [], 0,
                $"the question consultant is switched off in this installation ({QuestionConsultKeys.Enabled}) — ask the person with ask_human");
        }

        if (options.RowsUnreadable)
        {
            return Error($"the question consultant's rows ({QuestionConsultKeys.Rows}) could not be read, so this installation does not know which models to ask — fix them in {QuestionAdmission.Section}. Nothing was sent anywhere");
        }

        if (Arguments(question, contextText) is { Length: > 0 } refused)
        {
            return Error(refused);
        }

        var (repo, refusal) = await context.TopLevelAsync(repoPath, ct);
        if (refusal.Length > 0)
        {
            return Error(refusal);
        }

        var (sha, branch) = await context.HeadAsync(repo, ct);
        var record = NewRecord(repo, branch, sha, question.Trim(), contextText.Trim(), sessionOf(repo, branch, document, feature));

        return Reply(await InTheRepositoryAsync(record, ct));
    }

    /// <summary>
    /// D8: the consultants BESIDE a person's card. The question is the one the person was asked, the context the
    /// caller's declared risk, the record marked as such and bound to the card; the switch, unreadable rows and a
    /// path that is no repository answer nothing (null) rather than a refusal — the person was already asked.
    /// A question or context past the tool's limits is CUT to them, marked <see cref="CutMarker"/>, and the record
    /// says so in <see cref="QuestionConsultRecord.Truncated"/>: refusing it here meant no consultant ran at all.
    /// </summary>
    /// <param name="endedAs">How the record says the question ended when somebody answered: <see cref="QuestionOutcomes.ProductionRisk"/>
    /// beside a held gate's card, <see cref="QuestionOutcomes.ProductionRiskConsultedFirst"/> when the consultants ran before
    /// the AI asked in its conversation (<c>todo/PLAN_ask_human_is_for_the_gate.md</c>, G3).</param>
    public async Task<QuestionConsultRecord?> BesideAsync(
        string repoPath, string question, string contextText, string sessionId, string escalationId, CancellationToken ct,
        string endedAs = QuestionOutcomes.ProductionRisk)
    {
        var options = settings.QuestionConsult;
        var fitted = Fitted.Of(question.Trim(), contextText.Trim());
        if (!options.Enabled || options.RowsUnreadable || Arguments(fitted.Question, fitted.Context).Length > 0)
        {
            return null;
        }

        var (repo, refusal) = await context.TopLevelAsync(repoPath, ct);
        if (refusal.Length > 0)
        {
            return null;
        }

        var (sha, branch) = await context.HeadAsync(repo, ct);
        var record = NewRecord(repo, branch, sha, fitted.Question, fitted.Context, sessionId) with
        {
            ProductionRisk = true,
            RiskReason = contextText.Trim(),
            EscalationId = escalationId,
            Truncated = fitted.Truncated,
        };
        var consulted = await InTheRepositoryAsync(record, ct);
        // The outcome says how the question ENDED: beside the person's card, unless nobody could be asked at all.
        var ended = consulted.Record.Outcome is QuestionOutcomes.QuotaSpent or QuestionOutcomes.NoneAvailable
            ? consulted.Record
            : consulted.Record with { Outcome = endedAs };
        _store.Write(ended);

        return ended;
    }

    private QuestionConsultRecord NewRecord(string repo, string branch, string sha, string question, string contextText, string sessionId) =>
        new(QuestionConsultStore.NewId(), ConsultationService.CallerOf(env, repo), CallerIdentity.KindFrom(env), sessionId,
            repo, branch, sha, question, QuestionConsultStore.Stamp(DateTime.UtcNow))
        {
            Context = contextText,
        };

    /// <summary>What one question came to: the reply's parts, and the record as it ended.</summary>
    private sealed record Consulted(QuestionConsultRecord Record, string Status, IReadOnlyList<QuestionRowAnswer> Answers, int Left, string Next);

    /// <summary>What ends a question or context cut to the limits beside a person's card.</summary>
    public const string CutMarker = "[…truncated]";

    /// <summary>A question and its context cut to <see cref="MaxQuestionBytes"/> and <see cref="MaxContextBytes"/>, and the sentence saying what was cut.</summary>
    private sealed record Fitted(string Question, string Context, string Truncated)
    {
        public static Fitted Of(string question, string contextText)
        {
            var (q, questionCut) = Core.Feature.FeatureContext.Within(question, MaxQuestionBytes, CutMarker);
            var (c, contextCut) = Core.Feature.FeatureContext.Within(contextText, MaxContextBytes, CutMarker);
            string[] said =
            [
                .. questionCut ? [$"the question was {Encoding.UTF8.GetByteCount(question)} bytes, cut to {MaxQuestionBytes}"] : Array.Empty<string>(),
                .. contextCut ? [$"the context was {Encoding.UTF8.GetByteCount(contextText)} bytes, cut to {MaxContextBytes}"] : Array.Empty<string>(),
            ];

            return new Fitted(q, c, string.Join("; ", said));
        }
    }

    /// <summary>The argument checks, each a sentence naming the cure: an empty context by NAME, the two sizes.</summary>
    internal static string Arguments(string question, string contextText)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return "a question is required — the consultants are asked what you would ask the person";
        }

        if (string.IsNullOrWhiteSpace(contextText))
        {
            return "context is required — say what you tried and what the options are; a question with no context is answered from nothing, and the person would be asked the same";
        }

        return Encoding.UTF8.GetByteCount(question) > MaxQuestionBytes
            ? $"the question is {Encoding.UTF8.GetByteCount(question)} bytes and may be at most {MaxQuestionBytes} — a question is a sentence or a paragraph; put the rest in context"
            : Encoding.UTF8.GetByteCount(contextText) > MaxContextBytes
                ? $"the context is {Encoding.UTF8.GetByteCount(contextText)} bytes and may be at most {MaxContextBytes} — what was tried and the options, not the code"
                : string.Empty;
    }

    private async Task<Consulted> InTheRepositoryAsync(QuestionConsultRecord record, CancellationToken ct)
    {
        var options = settings.QuestionConsult;
        var caller = record.Caller;
        if (options.Rows.Count(r => r.Enabled) == 0)
        {
            return Written(record, QuestionOutcomes.NoneAvailable, Statuses.NoneAvailable, Left(caller, options),
                $"no question-consultant row is switched on — add one in {QuestionAdmission.Section}, or ask the person with ask_human");
        }

        // The quota is taken LAST before the launches, so a refusal above spends nothing — and once,
        // for the question, however many rows it fans out to.
        var counted = _counter.TryTake(QuotaKey(caller), options.QuestionsPerSession, DateTime.UtcNow);
        if (!counted.Allowed)
        {
            return Written(record, QuestionOutcomes.QuotaSpent, Statuses.QuotaSpent, 0,
                $"this caller session has asked the consultants {counted.Used} questions, the cap ({QuestionConsultKeys.QuestionsPerSession} = {options.QuestionsPerSession}; "
                + $"the window is {ConsultCallCounter.Window.TotalHours:0} hours from the first) — ask the person with ask_human");
        }

        return await FannedOutAsync(record, Math.Max(0, options.QuestionsPerSession - counted.Used), counted.Note, ct);
    }

    private async Task<Consulted> FannedOutAsync(QuestionConsultRecord record, int left, string counterNote, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var fanOut = new QuestionFanOut(launcher, executor, _store, prompts, ledger, keys, settings, new Normalizer.TreeSitterOutliner(), _answerSchema.Path, env, log);
        log.Information("question {Id}: {Rows} row(s) configured, asked in {Repo}", record.Id, settings.QuestionConsult.Rows.Count, record.RepoPath);
        var settled = await fanOut.RunAsync(record, new FanOutInput(
            settings.QuestionConsult, settings.Providers, settings.ApiOverrides, record.Question, record.Context,
            record.RepoPath, record.HeadSha, settings.FeatureSourceFollowUps, nonce), ct);

        var answers = settled.Rows.Select(row => Answer(row, nonce)).ToList();
        var launched = settled.Rows.Count(r => r.Status is not (RowOutcomes.Blocked or RowOutcomes.Disabled or RowOutcomes.Refused));

        return new Consulted(settled, StatusOf(settled, launched), answers, left, Next(settled, launched, counterNote));
    }

    private static string StatusOf(QuestionConsultRecord settled, int launched) =>
        launched == 0 ? Statuses.NoneAvailable
        : settled.Status == QuestionConsultStatuses.Answered ? Statuses.Complete
        : settled.Status == QuestionConsultStatuses.Partial ? Statuses.Partial
        : Statuses.Failed;

    private static string Next(QuestionConsultRecord settled, int launched, string counterNote)
    {
        var answered = settled.Rows.Count(r => r.Answered);
        var sentence = launched == 0
            ? "no row could be asked (each says why) — fix the rows or the question, or ask the person with ask_human"
            : answered == 0
                ? "no consultant answered (each row says why) — ask the person with ask_human"
                : $"{answered} of {launched} consultant(s) answered: verify each answer before acting on it, and ask the person with ask_human only if the answers do not settle the question";

        return counterNote.Length > 0 ? sentence + " (" + counterNote + ")" : sentence;
    }

    /// <summary>
    /// Every row of a settled question as the AI reads it — the same fence <c>ask_consultants</c> answers with, under a
    /// nonce of its own: the reply of an <c>ask_human</c> whose consultants ran first (G3) is another model's advice too.
    /// </summary>
    internal static IReadOnlyList<QuestionRowAnswer> Fenced(QuestionConsultRecord record)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];

        return [.. record.Rows.Select(row => Answer(row, nonce))];
    }

    /// <summary>One row's answer, fenced <c>advisory_only</c> with its own vendor and model — never merged (D2).</summary>
    private static QuestionRowAnswer Answer(QuestionRowRecord row, string nonce) => new(
        row.RowId, row.Vendor, row.Model, row.PromptTitle, row.Capability, row.Flag, row.Status, row.Seconds, row.CostUsd, row.Reason, row.Note,
        row.Answered ? ConsultationFence.Advice(row.Vendor, row.Model, new TurnBudget(1, 0), nonce, row.Advice) : string.Empty);

    /// <summary>A question that ends before any launch: written down with its outcome, so the log says it went straight to the person.</summary>
    private Consulted Written(QuestionConsultRecord record, string outcome, string status, int left, string next)
    {
        var stamp = QuestionConsultStore.Stamp(DateTime.UtcNow);
        var ended = record with { Status = QuestionConsultStatuses.Failed, Outcome = outcome, UpdatedUtc = stamp, EndedUtc = stamp };
        _store.Write(ended);

        return new Consulted(ended, status, [], left, next);
    }

    private int Left(string caller, QuestionConsultSettings options) => options.QuestionsPerSession;

    /// <summary>The quota's own key beside the stuck consultant's: one caller, two counters.</summary>
    internal static string QuotaKey(string caller) => "q:" + caller;

    private static string Reply(string id, string status, IReadOnlyList<QuestionRowAnswer> answers, int left, string next) =>
        JsonSerializer.Serialize(new AskConsultantsAnswer(id, status, answers, left, next), ServerJsonContext.Default.AskConsultantsAnswer);

    private static string Reply(Consulted consulted) =>
        Reply(consulted.Record.Id, consulted.Status, consulted.Answers, consulted.Left, consulted.Next);

    /// <summary>A refusal, through the ONE place the wire shape is built. See <see cref="Refusal"/>.</summary>
    private string Error(string sentence, [CallerMemberName] string from = "") =>
        Refusal.Answer(sentence, noticing, from);
}
