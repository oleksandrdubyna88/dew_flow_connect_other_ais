using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server;

/// <summary>
/// What the host does with an exception nothing else caught: write it down, say it REDACTED, exit
/// non-zero.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> <c>ServeAsync</c> caught two types and <c>Main</c> caught nothing, so
/// any third exception escaped the process and the RUNTIME printed it — unredacted — to stderr, which
/// is the channel an MCP client captures as the server's log. A reviewer failure carrying a bearer
/// token or an authority URL in its message reached that channel in clear, and the page learned
/// nothing. (Story 3.2; defect 4 of the plan.)</para>
///
/// <para><b>Two sinks, one redactor.</b> The notice is redacted at the line like every notice. The LOG
/// line is redacted here, and it is a string property — the exception OBJECT is never handed to the
/// logger, because <c>log.Error(e, …)</c> renders it raw and would put straight into the file log the
/// secret the notice just took out. That is <c>security.md</c>'s named defect, applied to two sinks.</para>
///
/// <para><b>The record is written through the CONFIRMED road</b>, <see cref="ServerNotices.Append"/>,
/// rather than offered to the queue: the queue can accept a notice and then fail to write it, and the
/// host decides whether to clear its run marker by whether the death is ON DISK. A marker cleared
/// over a crash that was never written is a death that disappears.</para>
/// </remarks>
internal static class HostCrash
{
    /// <summary><c>EX_SOFTWARE</c>: an internal software error, from <c>sysexits.h</c>.</summary>
    internal const int ExitCode = 70;

    /// <summary>
    /// How much of a redacted exception the log keeps — a deep stack, not a flood.
    /// </summary>
    internal const int LogLimit = 16 * 1024;

    /// <summary>Say it, redacted, and answer the exit code.</summary>
    internal static int Handled(Exception crash, Serilog.ILogger log)
    {
        log.Error("the server stopped on an unexpected {Type}: {Detail}",
            crash.GetType().FullName, Logged(crash));

        return ExitCode;
    }

    /// <summary>The exception as the log may hold it: redacted, bounded, and a STRING.</summary>
    internal static string Logged(Exception crash) => Redaction.SafeText(crash.ToString(), LogLimit);

    /// <summary>
    /// <c>Main</c>'s last resort: a throw before there is a logger, said on stderr, redacted, exit 70.
    /// </summary>
    /// <remarks>
    /// <para>The second layer the plan's correction 2 names. The first moment a server can fail is
    /// resolving its data directory, and the logger is rooted IN that directory — so an unusable
    /// <c>COAI_DATA_SIDE</c>, whose message quotes the value it refused, used to reach stderr through
    /// the runtime, raw. There is no notice here: the directory a notice would go to may be the very
    /// thing that failed, and a guess at another is writing somebody else's data into it.</para>
    /// <para>The redactor is a parameter because it can be what failed: the word list loads lazily, and
    /// a published build that dropped it throws on first use. The fallback then is the TYPE and not the
    /// message — an unredacted message is never what a failed redaction degrades to.</para>
    /// </remarks>
    internal static int Unlogged(Exception crash, TextWriter stderr, Func<string, string> redact)
    {
        stderr.WriteLine($"{Runners.Reviewers.ShimNotes.Prefix}the server stopped before its log was open, "
            + $"on an unexpected {crash.GetType().FullName}: {Said(crash, redact)}");

        return ExitCode;
    }

    /// <summary>The production redactor, bounded as the log is.</summary>
    internal static string Redacted(string text) => Redaction.SafeText(text, LogLimit);

    private static string Said(Exception crash, Func<string, string> redact)
    {
        try
        {
            return redact(crash.ToString());
        }
        catch (Exception)
        {
            return "its message is withheld, because the redactor could not run";
        }
    }

    /// <summary>The crash as a notice: its type, its message and its frames.</summary>
    /// <remarks>
    /// Subject is the exception's full type name, so a server that keeps dying the same way is one row
    /// with a count rather than a page of them. Bounded where it is built, like every notice.
    /// </remarks>
    internal static ServerNotice Of(Exception crash, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = "coai-mcp",
        Code = ServerNoticeCodes.Crash,
        Subject = crash.GetType().FullName ?? crash.GetType().Name,
        Title = ServerNotice.Shortened($"{crash.GetType().Name}: {crash.Message}"),
        Detail = ServerNotice.Shortened(crash.ToString(), Redaction.DetailLimit),
    };

    /// <summary>
    /// Write the crash down through the confirmed road, within <paramref name="budget"/>.
    /// </summary>
    /// <remarks>
    /// Bounded because a wedged share must not keep a dying process alive. The wait only stops
    /// WAITING — the write it gave up on still holds its thread — which is acceptable here and nowhere
    /// else: the process is leaving, and takes that thread with it.
    /// </remarks>
    /// <returns>Whether the record is known to be on disk.</returns>
    internal static bool Recorded(ServerNotice crash, Func<ServerNotice, bool> append, TimeSpan budget)
    {
        var writing = Task.Run(() => append(crash));
        try
        {
            return writing.Wait(budget) && writing.Result;
        }
        catch (AggregateException)
        {
            return false;
        }
    }

    /// <summary>
    /// Dispose the host's logger — which is its flush — without letting a failing flush replace the
    /// exception being reported.
    /// </summary>
    /// <remarks>
    /// The rule (<c>logging-serilog.md</c>) says <c>Log.CloseAndFlush()</c> in a <c>finally</c>. That
    /// presumes Serilog's STATIC logger, and this host never assigns it: <c>CoaiLogging</c> hands it a
    /// local one, a deviation that class documents. So the rule's intent here is this — the host's own
    /// logger flushed on every exit, crash included, under a guard. There is nowhere left to say a
    /// failed flush except stderr, which on a stdio host is exactly where the console log goes.
    /// </remarks>
    internal static void Flushed(IDisposable log)
    {
        try
        {
            log.Dispose();
        }
        catch (Exception failure)
        {
            // The prefix `Program.Note` uses, so a person reading stderr finds it with the server's others.
            Console.Error.WriteLine($"{Runners.Reviewers.ShimNotes.Prefix}the log could not be flushed: "
                + Redaction.SafeText(failure.Message, Redaction.TitleLimit));
        }
    }
}
