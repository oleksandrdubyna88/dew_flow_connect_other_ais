using System.Text.Json;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--round-counts [--now &lt;utc instant&gt;]</c>: how many rounds each vendor row ran in the trailing
/// 5 h, 24 h and 7 d, by stage, from the rounds database — for the extension's Spending tab (story 2.1
/// of <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Why a mode.</b> The extension owns no SQLite and never speaks MCP to this binary; it drives
/// one-shot modes selected from <c>args[0]</c> before any transport opens — the door <c>--log</c> uses,
/// and the counter is the same database read a different way. Tokens and money are the EXTENSION's,
/// computed from the ledger it already reads; this answers rounds, and only from <c>coai.db</c>, so it
/// never re-scans a year of <c>usage.jsonl</c> on a refresh (the design decision of 2026-10-10,
/// recorded in the plan's deviations).</para>
/// <para><b>Exit codes.</b> <b>0</b> with the JSON on stdout — an EMPTY answer when the database does
/// not exist yet, which the extension tells from "unavailable" by the exit code and the parse: a count
/// is any exit 0 that parses, rows or none; unavailable is 64, a timeout, or text that will not parse.
/// <b>65</b> for a bad argument (an unparseable or repeated <c>--now</c>, an unknown flag), the sentence
/// on stderr naming the mode and what it takes. <b>74</b> for a database that will not read. <b>Never
/// 64</b>: that is "this binary has never heard of the mode", how the extension spots a server older
/// than the counter, and a request fault wearing it would send the extension down that fallback with
/// the fault hidden behind it (<c>.agents/PROJECT.md</c>).</para>
/// <para><b>Nothing is written.</b> The read is <c>RoundCounts.Read</c>'s read-only open; a reader cannot
/// migrate and must not try — the server's own open is what advances the schema.</para>
/// <para>A unit of its own rather than more of <c>Program.cs</c>, for the reason <c>CadenceReadMode</c> gives.</para>
/// </remarks>
internal static class RoundCountsMode
{
    internal const string Usage =
        "--round-counts takes at most `--now <ISO-8601 UTC instant>` (such as --now 2026-10-10T12:00:00Z), once, and nothing else";

    /// <param name="env">The environment the data directory is resolved from, exactly as <c>--log</c> resolves it.</param>
    /// <param name="output">stdout — the answer, and nothing else ever.</param>
    /// <param name="note">stderr — a refusal's sentence, through the channel this binary already prefixes.</param>
    /// <param name="clock">The clock a bare <c>--round-counts</c> ends its windows at; <c>--now</c> replaces it.</param>
    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note, TimeProvider clock)
    {
        var (ok, nowUtc, why) = NowOf(args, clock);
        if (!ok)
        {
            note(why);

            // 65, NOT 64: the binary HAS this mode and is refusing the request. `--close-consult`,
            // `--cadence` and `--log --since` answer a malformed request the same way.
            return 65; // EX_DATAERR
        }

        return Answer(PanelSettings.FromEnvironment(env).DataDir, nowUtc, output, note);
    }

    private static int Answer(string dataDir, DateTime nowUtc, TextWriter output, Action<string> note)
    {
        try
        {
            output.WriteLine(JsonSerializer.Serialize(RoundCounts.Read(dataDir, nowUtc), ServerJsonContext.Default.RoundCountsAnswer));

            return 0;
        }
        catch (Exception e) when (Program.Unreadable(e))
        {
            // A database that will not read is the disk, not the request, and not an empty count:
            // `--log --paged` answers it with the same code and the same sentence.
            note(Program.WhyUnreadable(e));

            return 74; // EX_IOERR
        }
    }

    /// <summary>
    /// The instant the windows end at: the clock, or exactly one <c>--now</c>. Anything else is a
    /// refusal with the usage — an unknown flag silently ignored would be a count about the wrong request.
    /// </summary>
    internal static (bool Ok, DateTime NowUtc, string Why) NowOf(string[] args, TimeProvider clock) =>
        args.Length switch
        {
            1 => (true, clock.GetUtcNow().UtcDateTime, string.Empty),
            3 when args[1] == "--now" => Instant(args[2]),
            _ => (false, default, Usage),
        };

    private static (bool Ok, DateTime NowUtc, string Why) Instant(string text) =>
        Program.TryParseInstant(text, out var at)
            ? (true, at.UtcDateTime, string.Empty)
            : (false, default, $"--round-counts --now needs an ISO-8601 UTC instant, such as 2026-10-10T12:00:00Z; '{text}' is not one");
}
