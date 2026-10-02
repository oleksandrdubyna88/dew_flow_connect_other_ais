using System.Diagnostics;
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
    TimeSpan Budget);

/// <summary>
/// One row of a question, launched: the planned confinement composed by the row's own adapter, one
/// shot — or, on an <c>api</c> row, the turns <see cref="IAnsweringFollowUps"/> asks for — under the row's
/// deadline, every launch on the ledger, the scratch directory gone afterwards.
/// </summary>
/// <remarks>
/// <para>A row is a <see cref="QuestionRowRecord"/> when it ends, whatever ended it (A1): the launcher's
/// own kill at the budget is <c>timed_out</c>; our backstop deadline firing is <c>timed_out</c> too, told
/// apart from the caller's cancellation by the token's STATE, never the exception's type; a contract
/// violation in <c>Build</c> — a plan this adapter cannot take — is <c>failed</c> with the sentence, so a
/// misconfigured row never takes the fan-out down.</para>
/// <para>The scratch directory is the row's own and is removed in <c>finally</c>: <c>coai-question-</c>
/// is in <c>shared/temp-sweep.json</c>'s never-swept list so a test sweep leaves a live row's alone.</para>
/// </remarks>
public sealed class QuestionRowLaunch(ReviewerExecutor executor, UsageLedger ledger, Serilog.ILogger log)
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
            return await TurnsAsync(input, Launch(input, scratch), start, started, row.Token);
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

    private static ConsultantLaunch Launch(RowLaunchInput input, string scratch) =>
        new(input.Repo, input.Prompt, string.Empty, input.AnswersDir, input.Settings, input.SchemaFile)
        {
            Confinement = new LaunchConfinement.Planned(input.Row.Plan),
            ScratchDir = scratch,
        };

    /// <summary>The launches of one row: one for a CLI, as many as the api row's follow-ups ask for.</summary>
    private async Task<QuestionRowRecord> TurnsAsync(RowLaunchInput input, ConsultantLaunch launch, QuestionRowRecord start, Stopwatch started, CancellationToken ct)
    {
        var memory = AnsweringMemory.Start(launch.Prompt);
        var usage = Usage.None;
        var note = string.Empty;
        while (true)
        {
            var turnStarted = started.Elapsed;
            var launched = await executor.LaunchAsync(input.Row.Runtime.Build(launch), ct);
            usage = usage.Add(launched.Usage);
            Record(input, launched, started.Elapsed - turnStarted);
            if (Unanswered(launched) is { } ended)
            {
                return Ended(start, ended.Status, ended.Reason, started.Elapsed, usage, note);
            }

            var answer = launched.Answer!;
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

    /// <summary>A launch that produced no answer, as the row's ending — or null when it answered.</summary>
    private static (string Status, string Reason)? Unanswered(ReviewerLaunch launched) =>
        launched.Terminal is ReviewerOutcome.TimedOut ? (RowOutcomes.TimedOut, "the row ran past its budget and its process was ended")
        : launched.Terminal is { } terminal ? (RowOutcomes.Failed, ReviewerSummaryFactory.Describe(terminal))
        : string.IsNullOrWhiteSpace(launched.Answer) ? (RowOutcomes.Failed, "the consultant exited cleanly but answered nothing")
        : null;

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

    /// <summary>One ledger line per launch — kind <c>question</c>, stage <c>Question</c> — so the Logs tab can total a row.</summary>
    private void Record(RowLaunchInput input, ReviewerLaunch launched, TimeSpan elapsed) =>
        ledger.RecordJob(
            string.Empty, input.Row.Provider.Provider, input.Settings.Model, ConsultantRoles.Question,
            launched.Terminal is { } terminal ? ReviewerSummaryFactory.Describe(terminal) : "ok",
            elapsed, launched.Usage.TokensIn, launched.Usage.TokensOut, launched.Usage.CostUsd, UsageKinds.Question, stage: "Question");

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
