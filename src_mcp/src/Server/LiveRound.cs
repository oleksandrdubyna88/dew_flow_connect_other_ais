using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The round as it happens: written to the session file before the first CLI starts, advanced as
/// each reviewer moves, and closed with the verdict and what it all consumed.
/// </summary>
/// <remarks>
/// <para>This is the durable-status rule pointed at our own slowest operation. A code round takes
/// minutes; until it was persisted at the START, the panel could not tell "six reviewers are
/// working" from "nothing has ever run here" — and the person watching had to read a log to find
/// out which.</para>
/// <para>The live record is not the source of the VERDICT — the state machine is. It is the
/// answer to "what is happening right now", which is a different question and has to survive an
/// F5, a restarted extension and a killed server.</para>
/// </remarks>
public sealed class LiveRound
{
    private readonly SessionStore _store;
    private readonly PersistedSession _session;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ReviewerState> _states;
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    private readonly string _subject;

    private readonly Noticing _noticing;

    /// <summary>
    /// The number this round is written under, in the session file and in the database.
    /// </summary>
    /// <remarks>
    /// Handed in, never derived here. This read <c>RoundsRunThisStage + 1</c>, and that counter is
    /// the BUDGET's — reset to zero by <c>again</c>, by the escalation ladder and by a person's
    /// Continue/Fix — so the round after any of them took the number of the round before it and
    /// replaced its row (§9.6 of the feature-review plan). The number comes from the journal
    /// (<see cref="RoundNumber"/>), and the caller allocates it under the session claim.
    /// </remarks>
    private readonly int _number;

    /// <summary>
    /// Which reviewers have had their ending written down, so a second report does not write a second line.
    /// </summary>
    /// <remarks>
    /// The scheduler reports a terminal outcome once per reviewer today — every road returns after
    /// its one <c>Report(..., "failed", outcome, ...)</c> — but this is a contract of the PUBLIC
    /// <see cref="Report"/>, which <c>LiveRoundTests</c> already drives twice for one reviewer. A key
    /// is added only after the offer is ACCEPTED, so a write the disk refused is retried rather than
    /// suppressed. (codex, on the plan round.)
    /// </remarks>
    private readonly HashSet<string> _noticed = new(StringComparer.Ordinal);

    /// <param name="noticing">
    /// Where a reviewer that did not answer is written down. Required and undefaulted: a defaulted
    /// one is the trap the plan round named, where production takes the quiet path while every
    /// injected test passes.
    /// </param>
    /// <param name="number">The round's number in this session's journal for its stage — see <see cref="_number"/>.</param>
    public LiveRound(
        SessionStore store,
        PersistedSession session,
        int number,
        IReadOnlyList<ReviewerWork> work,
        string subject,
        Noticing noticing)
    {
        _subject = subject;
        _noticing = noticing;
        _number = number;
        _store = store;
        _session = session;
        _states = work.ToDictionary(
            w => Key(w.Invocation.Provider, w.Invocation.Role),
            w => new ReviewerState(
                w.Invocation.Provider,
                w.Invocation.Role,
                ReviewerState.Queued,
                // This side was right from the start; the invocation was not. Only LocalRuntime and
                // RemoteRuntime ever PASSED a model, so codex, gemini, claude and antigravity wrote
                // an empty one here for as long as the field existed — the log named WHO reviewed
                // and not WITH WHAT for exactly the vendors people ask about (issue #129). The four
                // adapters set it now, and `EveryAdapterRecordsWhatItLaunchedTests` goes through
                // each one's own `Build` rather than a hand-made invocation, which is what let the
                // first version of this ship believing itself covered.
                Model: Normalise(w.Invocation.Model),
                // The effort that was APPLIED, which today only a local launch has.
                Effort: Normalise(w.Invocation.Effort)));
        Persist();
    }

    /// <summary>
    /// The reviewer's state after this report — the merge, with no I/O and no lock in it.
    /// </summary>
    /// <remarks>
    /// Lifted out of <see cref="Report"/> so that method is orchestration and this one is the rule.
    /// (CodeRabbit, on story 2.3.2's pull request: <c>Report</c> was over the complexity ceiling
    /// `.coderabbit.yaml` sets for this project, and it was over it before this story touched it.)
    /// </remarks>
    private static ReviewerState Moved(ReviewerState previous, ReviewerProgress progress) => previous with
    {
        Status = progress.Status,
        Findings = progress.Outcome is ReviewerOutcome.Ok ok ? ok.Review.Findings.Count() : previous.Findings,
        Note = Noted(previous, progress),
        // Only a FINISHED reviewer has a duration; a "running" report carries zero, and taking it
        // would erase the number of one that had already finished.
        Seconds = progress.Elapsed > TimeSpan.Zero
            ? Math.Round(progress.Elapsed.TotalSeconds, 1)
            : previous.Seconds,
    };

    /// <summary>
    /// The sentence this reviewer carries: its failure, or its own note, or what it already had.
    /// </summary>
    /// <remarks>
    /// A progress line may carry its own sentence — a queued reviewer saying what it is waiting for
    /// — and it is only ever an ADDITION: an empty one never wipes the reason a failed reviewer
    /// already recorded.
    /// </remarks>
    private static string Noted(ReviewerState previous, ReviewerProgress progress)
    {
        if (progress.Outcome is { } outcome and not ReviewerOutcome.Ok)
        {
            return ReviewerSummaryFactory.Describe(outcome);
        }

        return progress.Note.Length > 0 ? progress.Note : previous.Note;
    }

    /// <summary>
    /// A reviewer that did not answer, written down once.
    /// </summary>
    /// <remarks>
    /// <para>Here rather than in <c>Describe</c>: the parent plan forbids instrumenting that, because
    /// it lives in <c>runners</c> and would make that assembly know about the ledger. This is where
    /// the outcome is OBSERVED, and the sentence is borrowed.</para>
    /// <para>A throw from here would not fail the round — the scheduler catches its own progress
    /// callback — but it WOULD skip the rest of that callback, which is this reviewer's audit line
    /// and its spending row (<c>PanelService.cs:1249-1265</c>). That is what
    /// <see cref="Noticing.Offered"/>'s boundary is protecting, and the split corrected the plan
    /// round's claim that a round would die.</para>
    /// </remarks>
    private void Noticed(ReviewerProgress progress, string key)
    {
        if (Ending(progress) is not { } outcome || _noticed.Contains(key))
        {
            return;
        }

        // Marked only after the writer ACCEPTED it, so a notice the disk refused is offered again on
        // the next report rather than suppressed by a key nothing wrote. (codex, on the plan round.)
        if (_noticing.Offered(() => ReviewerNotices.Of(progress.Provider, progress.Role, outcome)))
        {
            _noticed.Add(key);
        }
    }

    /// <summary>The outcome this report ends on, when it is one that gets written down.</summary>
    private static ReviewerOutcome? Ending(ReviewerProgress progress) =>
        progress.Outcome is { } outcome && ReviewerNotices.IsAFailure(outcome) ? outcome : null;

    /// <summary>A model, or nothing — the two ways of having none, made one.</summary>
    /// <remarks>
    /// <para>Trimmed, because a configured model of " " is not a model and the renderer's truthiness
    /// check would treat it as one: a separator with nothing after it.</para>
    /// <para>The null coalesce is NOT belt-and-braces on a non-nullable string. An invocation that
    /// came back through JSON with a null model IS null at runtime whatever the annotation says, and
    /// `.Trim()` on it would throw before the round is persisted or any reviewer starts — the one
    /// place in this file where a nullable-reference annotation is a claim rather than a
    /// guarantee.</para>
    /// </remarks>
    private static string Normalise(string? model) => (model ?? string.Empty).Trim();

    /// <summary>One reviewer moved. Called from the fan-out's threads, hence the lock.</summary>
    public void Report(ReviewerProgress progress)
    {
        lock (_gate)
        {
            var key = Key(progress.Provider, progress.Role);
            var previous = _states.GetValueOrDefault(key)
                           ?? new ReviewerState(progress.Provider, progress.Role, progress.Status);

            _states[key] = Moved(previous, progress);
            Noticed(progress, key);
            Persist();
        }
    }

    /// <summary>
    /// The finished record: the verdict, and the round's total usage folded from every reviewer
    /// that reported any — including a repaired reviewer's two launches.
    /// </summary>
    public RoundRecord Finish(
        string verdict,
        int gatingCount,
        string reviewers,
        IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results)
    {
        // Failures that still burned tokens count towards the round's total: an unparseable
        // answer is a completed run whose usage the vendor reported. Counting only `Ok` made a
        // round with two fallen reviewers report roughly half of what it actually cost.
        var usage = results
            .Select(r => r.Outcome switch
            {
                ReviewerOutcome.Ok ok => ok.Usage,
                ReviewerOutcome.Unparseable bad => bad.Usage,
                _ => Core.Findings.Usage.None,
            })
            .Aggregate(Core.Findings.Usage.None, (total, one) => total.Add(one));

        lock (_gate)
        {
            return Record(verdict, gatingCount, reviewers, RoundRecord.Done) with
            {
                CompletedUtc = DateTime.UtcNow,
                TokensIn = usage.TokensIn,
                TokensOut = usage.TokensOut,
                CostUsd = usage.CostUsd,
            };
        }
    }

    /// <summary>
    /// The running round is the LAST element of the trail while it runs, and is replaced by the
    /// finished record when the stage completes — never appended twice.
    /// </summary>
    private void Persist()
    {
        try
        {
            var running = Record("running", 0, RunningSentence(), RoundRecord.Running);
            _store.Save(_session with { Rounds = [.. _session.Rounds, running] });
        }
        catch (SessionStoreException)
        {
            // A missed repaint is not a failed review; the next progress event writes again. This
            // is the ONE save allowed to be lost, and it is why the store throws something NAMED:
            // this catch used to read `catch (IOException)` while the failure arrived as
            // UnauthorizedAccessException — not an IOException — so it walked straight past and
            // killed six code rounds, one of them with every reviewer already answered.
        }
    }

    private RoundRecord Record(string verdict, int gatingCount, string reviewers, string status) =>
        new(
            _session.State.Stage.ToString(),
            _number,
            verdict,
            gatingCount,
            reviewers,
            DateTime.UtcNow)
        {
            Status = status,
            StartedUtc = _startedUtc,
            RunnerPid = Environment.ProcessId,
            Subject = _subject,
            ReviewerStates = [.. _states.Values.OrderBy(s => s.Provider).ThenBy(s => s.Role)],
            // Taken from the session THIS round started from, so a later `open` by another client
            // cannot relabel it. See RoundRecord.Caller for what the two fields say apart.
            Caller = _session.Caller,
            // The epic this round is FOR, from its first write — so a round a crash interrupted still
            // says which epic it was, and the cadence never asks the same epic twice (todo/PLAN_consult_on_a_cadence.md).
            PlanKey = _session.Plan.Length > 0 ? Core.Cadence.EpicRef.PlanKey(_session.Plan) : string.Empty,
            EpicNumber = Core.Cadence.EpicRef.Parse(_session.Epic, _session.Plan) is Core.Cadence.EpicRef.Some epic ? epic.Number : 0,
        };

    private string RunningSentence()
    {
        var done = _states.Values.Count(s => s.Status is ReviewerState.Done);
        var failed = _states.Values.Count(s => s.Status is ReviewerState.Failed);
        var running = _states.Values.Count(s => s.Status is ReviewerState.Running);
        return $"{done} of {_states.Count} answered, {running} running" + (failed > 0 ? $", {failed} failed" : string.Empty);
    }

    private static string Key(string provider, string role) => $"{provider}/{role}";
}
