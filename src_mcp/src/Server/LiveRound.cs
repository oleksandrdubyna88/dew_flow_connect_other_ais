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

    public LiveRound(SessionStore store, PersistedSession session, IReadOnlyList<ReviewerWork> work, string subject = "")
    {
        _subject = subject;
        _store = store;
        _session = session;
        _states = work.ToDictionary(
            w => Key(w.Invocation.Provider, w.Invocation.Role.ToString()),
            w => new ReviewerState(w.Invocation.Provider, w.Invocation.Role.ToString(), ReviewerState.Queued));
        Persist();
    }

    /// <summary>One reviewer moved. Called from the fan-out's threads, hence the lock.</summary>
    public void Report(ReviewerProgress progress)
    {
        lock (_gate)
        {
            var key = Key(progress.Provider, progress.Role.ToString());
            var previous = _states.GetValueOrDefault(key)
                           ?? new ReviewerState(progress.Provider, progress.Role.ToString(), progress.Status);
            _states[key] = previous with
            {
                Status = progress.Status,
                Findings = progress.Outcome is ReviewerOutcome.Ok ok ? ok.Review.Findings.Count() : previous.Findings,
                Note = progress.Outcome is { } outcome and not ReviewerOutcome.Ok
                    ? ReviewerSummaryFactory.Describe(outcome)
                    // A progress line may carry its own sentence — a queued reviewer saying what it
                    // is waiting for — and it is only ever an ADDITION: an empty one never wipes
                    // the reason a failed reviewer already recorded.
                    : progress.Note.Length > 0
                        ? progress.Note
                        : previous.Note,
                // Only a FINISHED reviewer has a duration; a "running" report carries zero, and
                // taking it would erase the number of one that had already finished.
                Seconds = progress.Elapsed > TimeSpan.Zero
                    ? Math.Round(progress.Elapsed.TotalSeconds, 1)
                    : previous.Seconds,
            };
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
            _session.State.RoundsRunThisStage + 1,
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
