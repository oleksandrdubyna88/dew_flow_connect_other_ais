using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// Somewhere to write a notice, and the boundary that keeps writing one from costing anything.
/// </summary>
/// <remarks>
/// <para><b>Why a delegate and not the writer.</b> <see cref="NoticeWriter.Offer"/> by contract never
/// throws, so a test handed the concrete writer could not make this boundary fire — and a boundary no
/// test can reach is a guarantee nobody has checked, which is what Sonar said about story 2.2 and
/// what 2.3.1 then had to fix twice. The delegate is the seam.</para>
///
/// <para><b>Why it carries a logger.</b> <see cref="LiveRound"/> has none — its <c>Persist</c> catch
/// is silent by design — and a notice that is lost must still be said out loud. Passing the log with
/// the offer means the thing that knows a write failed is the thing that can report it.</para>
///
/// <para><b>What a throw here would cost, precisely.</b> Not the round: the scheduler already wraps
/// its progress callback in <c>catch (Exception)</c> — *"Reporting is not the work"*
/// (<c>BoundedScheduler.cs:359-376</c>). What it costs is the REST of the callback at
/// <c>PanelService.cs:1249-1265</c>, where <c>live.Report</c>, <c>audit.Moved</c> and
/// <c>_ledger.Record</c> run in sequence — so a throw in the first loses that reviewer's audit line
/// and its spending row. The split found that the plan round had this consequence wrong, and this is
/// the corrected claim.</para>
///
/// <para><b>One boundary, two callers.</b> Story 2.3.3's startup notes come through
/// <see cref="Offered"/> as well; a boundary specialised to reviewer failures is one 2.3.3 would have
/// to bypass or copy, which is the finding 2.2's code round made about the writer itself.</para>
/// </remarks>
/// <param name="Offer">Hands one notice to whatever writes them. Answers whether it was accepted.</param>
/// <param name="Log">Where a notice that did not land is said out loud.</param>
public sealed record Noticing(Func<ServerNotice, bool> Offer, Serilog.ILogger Log)
{
    /// <summary>The production composition: the host's writer, this side's directory, its log.</summary>
    internal static Noticing Through(NoticeWriter writer, Serilog.ILogger log) =>
        new(notice => writer.Offer(Where, notice, log), log);

    /// <summary>
    /// Where this side's data directory is — asked of the ONE resolver, from the ONE place notices ask.
    /// </summary>
    /// <remarks>
    /// It is a named method and it lives here rather than at each caller, because
    /// <c>TheOneAppendTests</c> asks the ASSEMBLY which members ANSWER a <see cref="ResolvedDataDir"/>
    /// — that census exists to catch a second IMPLEMENTATION of the rule. Composing this as a lambda
    /// at the call site put <c>&lt;&gt;c.&lt;ServeAsync&gt;b__46_4</c> into that list: a member with a
    /// name no reader can place, which is worse than the thing the census was watching for. One
    /// named pass-through, used by the refusals and by the reviewer failures alike.
    /// </remarks>
    internal static ResolvedDataDir Where() =>
        PanelSettings.DataDirectoryFor(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Writes nothing, says nothing — for a one-shot mode, and for a test that is about something else.
    /// </summary>
    /// <remarks>
    /// <c>--providers</c> and <c>--close-consult</c> build a <see cref="PanelService"/> to answer on
    /// stdout and exit; starting a writer thread for them would be a thread nobody drains. It is a
    /// property rather than a field so that a caller cannot hold one past the call.
    /// </remarks>
    public static Noticing None => new(_ => false, Serilog.Core.Logger.None);

    /// <summary>
    /// Builds a notice and offers it. Never throws, and never lets a failure be silent.
    /// </summary>
    /// <remarks>
    /// The record is built INSIDE the boundary because building one can throw — a code the catalog
    /// does not carry is an <c>ArgumentException</c> at construction — and a caller that built it
    /// outside would be a caller whose own line can fail.
    /// </remarks>
    public bool Offered(Func<ServerNotice> build)
    {
        try
        {
            return Said(Offer(build()));
        }
        catch (Exception failure)
        {
            // ONE sentence per lost notice. Routing this through `Said` too said it twice — the
            // exception and then "not accepted" — which reads in a log as two different losses.
            Quietly(() => Log.Warning(failure, "a notice could not be written down"));

            return false;
        }
    }

    /// <summary>An offer that was not accepted is reported, and the report is never the failure.</summary>
    private bool Said(bool offered)
    {
        if (!offered)
        {
            Quietly(() => Log.Warning("a notice was not accepted by the writer"));
        }

        return offered;
    }

    /// <summary>
    /// A log that throws must not become the silence it was there to prevent.
    /// </summary>
    /// <remarks>
    /// A disposed logger or a full sink throwing from inside the call that reports a lost notice
    /// would swallow both. There is nowhere left to say it — this process's stdout may be carrying a
    /// protocol — so what this buys is that the caller returns. (Story 2.2's code round, kept.)
    /// </remarks>
    private static void Quietly(Action saying)
    {
        try
        {
            saying();
        }
        catch (Exception)
        {
            // Nowhere left to say it.
        }
    }
}
