using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server;

/// <summary>Runs the consultation sweep while the server serves, not only when it starts.</summary>
/// <remarks>
/// <para>The sweep closes a consultation idle past its budget, fails or resumes one a dead process left
/// <c>asking</c>, and evicts the long finished. It ran only in the <see cref="PanelService"/> constructor,
/// so "Close an idle consultation after, minutes" held for a follow-up (refused) but not for the record,
/// which read <c>open</c> on the sidebar and in <c>status</c> until the next start
/// (<c>todo/PLAN_consult_limits_kinds_and_help.md</c>, story 1).</para>
/// <para>Through <paramref name="current"/> on every beat, never a service captured at start, so a
/// settings reload is swept by the service it built. A failed beat is logged and the next one tries
/// again, as <c>RunLife.Beaten</c> does: one bad directory read must not end the sweeping for good.</para>
/// </remarks>
public static class ConsultationSweeper
{
    /// <summary>How often: the budget is set in minutes, so the card is at most this late.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

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
            var swept = current().SweepConsultations();
            if (swept > 0)
            {
                log.Information("swept {Count} consultation(s): idle past their budget, interrupted by a dead process, or expired", swept);
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
