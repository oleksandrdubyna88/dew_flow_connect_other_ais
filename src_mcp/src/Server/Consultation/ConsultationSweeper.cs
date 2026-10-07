using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server;

/// <summary>Runs the consultation sweep while the server serves, not only when it starts.</summary>
/// <remarks>
/// <para>The sweep closes a consultation idle past its budget, fails or resumes one a dead process — or a
/// passed turn deadline — left <c>asking</c>, and evicts the long finished. It ran only in the <see cref="PanelService"/> constructor,
/// so "Close an idle consultation after, minutes" held for a follow-up (refused) but not for the record,
/// which read <c>open</c> on the sidebar and in <c>status</c> until the next start
/// (<c>research/PLAN_consult_limits_kinds_and_help.md</c>, story 1).</para>
/// <para>Through <paramref name="current"/> on every beat, never a service captured at start, so a
/// settings reload is swept by the service it built. A failed beat is logged and the next one tries
/// again, as <c>RunLife.Beaten</c> does: one bad directory read must not end the sweeping for good.</para>
/// </remarks>
public static class ConsultationSweeper
{
    /// <summary>
    /// The serving road: waits for the source's first service — the background start (<see cref="StartingHost"/>) —
    /// then beats through it. A start that did not succeed ends the sweeper before any beat; Program records it.
    /// </summary>
    /// <remarks>
    /// Taken by the sweeper rather than offered by the start (the code round, gemini, 2026-10-07): the start coordinates
    /// the start, and any other periodic job a server gains waits for it the same way, through the source.
    /// </remarks>
    public static async Task RunAsync(IPanelServiceSource source, TimeSpan every, Serilog.ILogger log, CancellationToken stop)
    {
        try
        {
            // The start alone — not CurrentAsync, which also rebuilds on a settings change and can fail for THAT reason,
            // which would have ended the sweeper for the life of the process (own review of the branch, 2026-10-07).
            await source.Ready.WaitAsync(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return; // serving ended before the start did
        }
        catch (Exception failure)
        {
            // The detached edge's catch-all: a start that did not succeed is recorded by Program as the crash it is;
            // here it only means there is nothing to sweep.
            log.Warning("consultation sweep: not started, the service was never built: {Why}",
                Redaction.SafeText(failure.Message, Redaction.TitleLimit));
            return;
        }

        // The start has completed, so every later CurrentAsync completes synchronously — this blocks nothing; it still
        // asks per beat, so a settings reload is swept by the service it built, and a call that throws is one failed
        // beat (Swept's catch), never the end of the sweeping.
        await RunAsync(() => source.CurrentAsync(stop).AsTask().GetAwaiter().GetResult(), every, log, stop).ConfigureAwait(false);
    }

    /// <param name="every">
    /// How often — <see cref="ServerPace.SweepEvery"/> (<c>COAI_SWEEP_SECONDS</c>, a minute unless set): the budget is set in
    /// minutes, so the card is at most this late. Each beat must stay cheap on an idle server: the escalation retention
    /// once read every session file per card on every beat, 40 % of a core (2026-10-06).
    /// </param>
    public static async Task RunAsync(Func<PanelService> current, TimeSpan every, Serilog.ILogger log, CancellationToken stop)
    {
        using var beat = new PeriodicTimer(every);
        try
        {
            while (await beat.WaitForNextTickAsync(stop))
            {
                Swept(current, log);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped, which is how serving ends.
        }
    }

    private static void Swept(Func<PanelService> current, Serilog.ILogger log)
    {
        try
        {
            var service = current();
            var swept = service.SweepConsultations();
            if (swept > 0)
            {
                log.Information("swept {Count} consultation(s): idle past their budget, interrupted by a dead process or a passed turn deadline, or expired", swept);
            }

            // The question consultant's records on the same beat (PLAN_question_consultant.md, S2): a
            // `consulting` record whose heartbeat stopped and whose server is gone, and the long finished.
            var questions = service.SweepQuestionConsults();
            if (questions > 0)
            {
                log.Information("swept {Count} question consultation(s): interrupted by a dead server, past retention, or over the file cap", questions);
            }

            // The person's cards and the phase records on the same beat (A4, S3): seven days for an answered or expired
            // question, an orphan or a temp file; thirty for a phase record; a held question is kept.
            var cards = service.SweepEscalations();
            if (cards > 0)
            {
                log.Information("swept {Count} escalation file(s) and question-phase record(s) past retention", cards);
            }
        }
        catch (Exception failure)
        {
            // The detached loop's catch-all: the next beat tries again.
            log.Warning("consultation sweep: a beat failed, and the next one will try again: {Why}",
                Redaction.SafeText(failure.Message, Redaction.TitleLimit));
        }
    }
}
