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
                    : previous.Note,
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
        // Per REVIEWER first, and the round's total folded from those — one traversal instead of
        // two, and the per-reviewer figure is what makes the round priceable at all. A round runs
        // several vendors at once on different rates, so its summed tokens have no single rate that
        // could price them; the panel prices each reviewer by the rate that actually applies to it
        // and marks the result as an estimate.
        //
        // Grouped rather than assigned per result, because a REPAIRED reviewer appears twice — the
        // relaunch is a second entry for the same provider and role, and taking the last one would
        // report half of what that reviewer really consumed.
        var perReviewer = results
            .GroupBy(r => Key(r.Invocation.Provider, r.Invocation.Role.ToString()))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(r => UsageOf(r.Outcome))
                    .Aggregate(Core.Findings.Usage.None, (total, one) => total.Add(one)));

        var usage = perReviewer.Values.Aggregate(Core.Findings.Usage.None, (total, one) => total.Add(one));

        lock (_gate)
        {
            foreach (var (key, spent) in perReviewer)
            {
                if (_states.TryGetValue(key, out var state))
                {
                    _states[key] = state with
                    {
                        TokensIn = spent.TokensIn,
                        TokensOut = spent.TokensOut,
                        CostUsd = spent.CostUsd,
                    };
                }
            }

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
    /// What one reviewer's run consumed. Failures that still burned tokens count: an unparseable
    /// answer is a completed run whose usage the vendor reported, and counting only <c>Ok</c> made
    /// a round with two fallen reviewers report roughly half of what it actually cost.
    /// </summary>
    private static Core.Findings.Usage UsageOf(ReviewerOutcome outcome) => outcome switch
    {
        ReviewerOutcome.Ok ok => ok.Usage,
        ReviewerOutcome.Unparseable bad => bad.Usage,
        _ => Core.Findings.Usage.None,
    };

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
        catch (IOException)
        {
            // A missed repaint is not a failed review; the next progress event writes again.
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
