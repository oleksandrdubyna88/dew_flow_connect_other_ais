using System.Diagnostics;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>What one row's launch is handed beside its admission: the prompt, the launch settings, the directory the record lives in.</summary>
/// <param name="Budget">The row's own deadline (<c>COAI_QCONSULT_ROW_MINUTES</c>): the launcher kills the child at it, and the turn is bounded a grace past it.</param>
public sealed record RowLaunchInput(
    RowAdmission.Admitted Row,
    string Prompt,
    ReviewerSettings Settings,
    string Repo,
    string AnswersDir,
    string SchemaFile,
    TimeSpan Budget)
{
    /// <summary>
    /// What has changed in the question's WATCHED roots since before any row launched — asked by
    /// <see cref="ConsultantTurn"/> before it continues a silent launch, so a row after which a root changed is not given
    /// a second chance. The fan-out hands its own comparison, over the roots it watches (none watched is no change).
    /// Required, so no caller can launch a row whose follow-up never asks.
    /// </summary>
    public required Func<CancellationToken, Task<IReadOnlyList<TreeChange>>> ChangesSoFar { get; init; }
}

/// <summary>One turn of a row: the launches it made — one, or a launch and its follow-up — and what stopped a follow-up.</summary>
/// <param name="Launches">Never empty; the last is the one the turn's answer stands on.</param>
/// <param name="ChangesBeforeFollowUp">The watched roots' changes that stopped a follow-up; empty when none did.</param>
/// <param name="Usage">The turn billed once (<see cref="ConsultantTurn.UsageOf"/>) — a cumulative vendor's two reports never summed.</param>
internal sealed record RowTurn(IReadOnlyList<ReviewerLaunch> Launches, IReadOnlyList<TreeChange> ChangesBeforeFollowUp, Usage Usage)
{
    public ReviewerLaunch Final => Launches[^1];

    public bool FollowedUp => Launches.Count > 1;
}

/// <summary>
/// One row of a question, launched: the planned confinement composed by the row's own adapter, one shot — continued
/// ONCE when it exited cleanly and said nothing in a way its adapter can cure, through <see cref="ConsultantTurn"/>, the
/// stuck consultant's own rule — or, on an <c>api</c> row, the turns <see cref="IAnsweringFollowUps"/> asks for — under
/// the row's deadline, every turn on the ledger, the scratch directory gone afterwards.
/// </summary>
/// <remarks>
/// <para>A row is a <see cref="QuestionRowRecord"/> when it ends, whatever ended it (A1): the launcher's
/// own kill at the budget is <c>timed_out</c>; our backstop deadline firing is <c>timed_out</c> too, told
/// apart from the caller's cancellation by the token's STATE, never the exception's type; a contract
/// violation in <c>Build</c> — a plan this adapter cannot take — is <c>failed</c> with the sentence, so a
/// misconfigured row never takes the fan-out down.</para>
/// <para><b>The follow-up</b> (research/PLAN_a_question_row_on_agy_is_continued_once.md): an antigravity <c>question-disk</c>
/// row reaches for <c>run_command</c> first, headless agy auto-denies it and the turn ends empty — measured 3 of 3 on
/// 2026-10-07, and 3 of 3 answered when continued with the adapter's follow-up. The conditions, "never a third launch"
/// and the lesser timeout are <see cref="ConsultantTurn"/>'s, not a second copy here.</para>
/// <para>The scratch directory is the row's own and is removed in <c>finally</c>: <c>coai-question-</c>
/// is in <c>shared/temp-sweep.json</c>'s never-swept list so a test sweep leaves a live row's alone.</para>
/// </remarks>
/// <param name="launcher">What a codex row's release is asked through before it launches (<see cref="TierProbedAsync"/>).</param>
public sealed class QuestionRowLaunch(ReviewerExecutor executor, UsageLedger ledger, Serilog.ILogger log, Runners.Processes.IProcessLauncher launcher)
{
    public const string ScratchPrefix = "coai-question-";

    public async Task<QuestionRowRecord> RunAsync(RowLaunchInput input, QuestionRowRecord start, CancellationToken ct)
    {
        var scratch = input.Row.Plan.Cwd == CwdKind.Scratch ? Directory.CreateTempSubdirectory("coai-question-").FullName : string.Empty;
        var started = Stopwatch.StartNew();
        using var row = CancellationTokenSource.CreateLinkedTokenSource(ct);
        row.CancelAfter(ConsultationDeadline.For(input.Budget));
        try
        {
            return await TurnsAsync(input, await TierProbedAsync(input.Row.Runtime, Launch(input, scratch), row.Token), start, started, row.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // OUR deadline, not the caller's — a wait the launcher did not bound (reliability.md).
            return Ended(start, RowOutcomes.TimedOut, $"the row ran past its {input.Budget.TotalMinutes:0.#}-minute budget and its grace", started.Elapsed, Usage.Unknown);
        }
        catch (ArgumentException e)
        {
            // The adapter refused the plan by name — a contract violation this row's configuration caused.
            return Ended(start, RowOutcomes.Failed, e.Message, started.Elapsed, Usage.None);
        }
        finally
        {
            Remove(scratch);
        }
    }

    /// <summary>
    /// A CODEX row's launch with its installed release asked first, through the codex adapter's own preparation — the
    /// stuck consultant's road, so the rule lives once (todo/PLAN_codex_tier_floor.md): an Off row on 0.110–0.130, which
    /// refuse <c>service_tier=default</c> at config load, is sent no tier. Inside the row's deadline, so a probe that hangs
    /// is bounded like the launch. Every other runtime's launch is unchanged — a claude row's own preparation is a
    /// separate open plan (todo/PLAN_a_question_row_on_an_old_claude.md), and a codex preparation never refuses.
    /// </summary>
    private async Task<ConsultantLaunch> TierProbedAsync(IConsultantRuntime runtime, ConsultantLaunch launch, CancellationToken ct) =>
        runtime is CodexConsultant codex && await codex.PrepareAsync(launch, launcher, ct) is ConsultantPreparation.Ready ready
            ? ready.Launch
            : launch;

    private static ConsultantLaunch Launch(RowLaunchInput input, string scratch) =>
        new(input.Repo, input.Prompt, string.Empty, input.AnswersDir, input.Settings, input.SchemaFile)
        {
            Confinement = new LaunchConfinement.Planned(input.Row.Plan),
            ScratchDir = scratch,
        };

    /// <summary>The turns of one row: one for a CLI (a launch, or a launch and its follow-up), as many as the api row's follow-ups ask for.</summary>
    private async Task<QuestionRowRecord> TurnsAsync(RowLaunchInput input, ConsultantLaunch launch, QuestionRowRecord start, Stopwatch started, CancellationToken ct)
    {
        var memory = AnsweringMemory.Start(launch.Prompt);
        var usage = Usage.None;
        var note = string.Empty;
        while (true)
        {
            var turn = await TurnAsync(input, launch, ct);
            usage = usage.Add(turn.Usage);
            if (Unanswered(input.Row.Runtime, turn) is { } ended)
            {
                return Ended(start, ended.Status, ended.Reason, started.Elapsed, usage, note);
            }

            var answer = turn.Final.Answer!;
            if (input.Row.Runtime is not IAnsweringFollowUps followUps)
            {
                return Answered(start, input.Row.Runtime.ReadAdvice(answer), started.Elapsed, usage, note);
            }

            switch (await followUps.AfterAsync(launch, memory, answer, ct))
            {
                case AnsweringTurn.Next next:
                    (launch, memory, note) = (next.Launch, next.Memory, next.Note);
                    continue;
                case AnsweringTurn.Done done:
                    return Answered(start, done.Advice, started.Elapsed, usage, Joined(note, done.Note));
            }
        }
    }

    /// <summary>
    /// One turn through <see cref="ConsultantTurn"/>, on ONE ledger line written in <c>finally</c> over whatever launches
    /// landed — so a turn that throws (the caller's cancellation, the row's backstop) cannot drop the first launch's usage
    /// (plan round, gemini). Every runtime a row resolves to is a consultant runtime (<see cref="QuestionResolution.For"/>).
    /// </summary>
    private async Task<RowTurn> TurnAsync(RowLaunchInput input, ConsultantLaunch launch, CancellationToken ct)
    {
        var consultant = input.Row.Runtime;
        // The row's system prompt is redacted from what the child says, as a reviewer's is (todo/PLAN_one_model_catalog.md, C2)
        // — on the follow-up too: an adapter continues `first with { … }`, which keeps it.
        var invocation = consultant.Build(launch) with { Redact = ConsultantTurnInputs.Redacted(input.Row.Provider.SystemPrompt) };
        var clock = Stopwatch.StartNew();
        var landed = new List<ReviewerLaunch>();
        var ended = false;
        try
        {
            var turned = await ConsultantTurn.RunAsync(executor, consultant, invocation, input.ChangesSoFar, landed.Add, ct);
            ended = true;
            SaySilentFirst(input, consultant, turned);

            return new RowTurn(turned.Launches, turned.ChangesBeforeFollowUp, turned.TurnUsage);
        }
        finally
        {
            if (landed.Count > 0)
            {
                // A turn that THREW has only the launches that landed before it, and the last of those may have exited
                // cleanly — its own outcome would read "ok" for a turn that never answered (code round, codex).
                Record(input, ended ? Outcome(landed[^1]) : Interrupted, ConsultantTurn.UsageOf(consultant, landed), clock.Elapsed);
            }
        }
    }

    /// <summary>Said in the log because the record keeps no launch count: the next "why did this row take two" is answered here.</summary>
    private void SaySilentFirst(RowLaunchInput input, IConsultantRuntime consultant, ConsultantTurnResult turned)
    {
        if (!turned.FollowedUp && !turned.Breached)
        {
            return;
        }

        log.Information("question row {Row} ({Vendor}): its first launch said nothing — {What}",
            input.Row.Row.Id, consultant.Vendor, turned.FollowedUp ? "continued once in the same conversation" : "not continued, a watched root changed");
    }

    /// <summary>The ledger's word for a turn cut short before it ended — the word the other records use for the same thing.</summary>
    private const string Interrupted = "interrupted";

    private static string Outcome(ReviewerLaunch final) =>
        final.Terminal is { } terminal ? ReviewerSummaryFactory.Describe(terminal) : "ok";

    /// <summary>A turn that produced no answer, as the row's ending — or null when it answered.</summary>
    private static (string Status, string Reason)? Unanswered(IConsultantRuntime runtime, RowTurn turn) =>
        turn.Final.Terminal is ReviewerOutcome.TimedOut ? (RowOutcomes.TimedOut, "the row ran past its budget and its process was ended")
        : turn.Final.Terminal is { } terminal ? (RowOutcomes.Failed, ReviewerSummaryFactory.Describe(terminal))
        : string.IsNullOrWhiteSpace(turn.Final.Answer) ? (RowOutcomes.Failed, Silent(runtime, turn))
        : null;

    /// <summary>
    /// Why a clean exit said nothing — and only what happened: the adapter's reading of its refusal when a follow-up
    /// RAN; the CLI's own last word when none did, with the changed roots when they are what stopped it.
    /// </summary>
    /// <remarks>
    /// An adapter's refusal sentence (agy's "did not answer even when told the command would not come") claims a
    /// follow-up; said of a turn that was never continued it would hide the change or the spent budget that stopped it
    /// (plan round, gemini). The <c>Cure</c> sentences are never used here: they point at the stuck consultant's tab.
    /// </remarks>
    private static string Silent(IConsultantRuntime runtime, RowTurn turn)
    {
        var empty = ConsultFailures.EmptyOf(turn.Final);
        if (turn.FollowedUp)
        {
            return runtime.SilentFailure(turn.Final, empty).What(runtime.Vendor);
        }

        return turn.ChangesBeforeFollowUp.Count > 0
            ? $"{empty.What(runtime.Vendor)}; it was not continued, because {FilesystemSnapshot.Sentence(turn.ChangesBeforeFollowUp)}"
            : empty.What(runtime.Vendor);
    }

    /// <remarks>
    /// The advice is what a MODEL wrote, and it is written down — the record, the database, the reply, the card — so it
    /// passes the product's redaction first (S4b item 3), the source resolver's road: <see cref="Redaction.SafeSource"/>
    /// keeps the layout, cuts nothing, and fails closed to <see cref="Redaction.Redacted"/>.
    /// </remarks>
    private static QuestionRowRecord Answered(QuestionRowRecord start, string advice, TimeSpan elapsed, Usage usage, string note) =>
        advice.Trim().Length == 0
            ? Ended(start, RowOutcomes.Failed, "the consultant answered, and its answer carried no advice", elapsed, usage, note)
            : Ended(start, RowOutcomes.Answered, string.Empty, elapsed, usage, note) with { Advice = Redaction.SafeSource(advice.Trim()) };

    /// <remarks>
    /// The reason can carry a CLI's stderr (<see cref="ReviewerSummaryFactory.Describe"/> keeps its tail), so it is a
    /// written-down text like the advice: one line through <see cref="Redaction.SafeText"/>, bounded at the detail limit.
    /// </remarks>
    private static QuestionRowRecord Ended(QuestionRowRecord start, string status, string reason, TimeSpan elapsed, Usage usage, string note = "") =>
        start with
        {
            Status = status,
            Reason = Redaction.SafeText(reason, Redaction.DetailLimit),
            Seconds = Math.Round(elapsed.TotalSeconds, 1),
            TokensIn = usage.TokensIn,
            TokensOut = usage.TokensOut,
            CostUsd = usage.CostUsd,
            Note = usage.NotCaptured ? Joined(note, CostText.UsageNotCaptured) : note,
            EndedUtc = QuestionConsultStore.Stamp(DateTime.UtcNow),
        };

    private static string Joined(string one, string other) =>
        one.Length == 0 ? other : other.Length == 0 ? one : one + "; " + other;

    /// <summary>
    /// One ledger line per TURN — kind <c>question</c>, stage <c>Question</c> — so the Logs tab can total a row; its
    /// outcome is the turn's last launch's — or <see cref="Interrupted"/> when the turn was cut short — and its usage the
    /// turn's own, billed once.
    /// </summary>
    /// <remarks>The whole <see cref="Usage"/>, not its three scalars: the cached count and the not-captured / no-price
    /// markers belong on the line too (PR #692, CodeRabbit).</remarks>
    private void Record(RowLaunchInput input, string outcome, Usage usage, TimeSpan elapsed) =>
        ledger.RecordJob(
            string.Empty, input.Row.Provider.Provider, input.Settings.Model, ConsultantRoles.Question,
            outcome, elapsed, usage, UsageKinds.Question, "Question");

    private void Remove(string scratch)
    {
        if (scratch.Length == 0)
        {
            return;
        }

        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Debug("a question row's scratch directory at {Path} could not be removed yet: {Reason}", scratch, e.Message);
        }
    }
}
