using System.Text.Json;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--round-counts-merged [--now &lt;utc instant&gt;] [&lt;absolute snapshot path&gt; …]</c>: the round counter over THIS side's rounds database and every
/// snapshot the other side's coai-mcp wrote, as one population (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Why a mode of its own.</b> For the reason <c>--round-counts</c> is one: a server too old for it must answer <b>64</b> so the extension falls
/// back to the single-store counter, which a flag on <c>--round-counts</c> would not do — an older binary would take the flag, ignore the paths and
/// answer one store's count as if it were the whole.</para>
/// <para><b>Exit codes.</b> <b>0</b> the answer — this side alone when no path is given (a valid request: the extension asks this mode whenever the
/// binary has it, because the launches-per-round figure needs its keys even with no other store). <b>65</b> a request fault: <c>--now</c> missing its
/// value, repeated or unparseable; an unknown flag; a relative path; more than 32 paths. <b>74</b> this side's database will not read. <b>Never 64</b>
/// (<c>.agents/PROJECT.md</c>), and <b>a snapshot problem never changes the code</b>: an unreadable, missing or newer snapshot is a row of
/// <c>storesNotIncluded</c>, because a store that could not be read is not a store of nought rounds.</para>
/// <para><b>Nothing is written.</b> This side's database is opened read-only, a snapshot is only read, and the data directory is resolved exactly as
/// <c>--round-counts</c> resolves it (<see cref="PanelSettings.FromEnvironment"/>).</para>
/// </remarks>
internal static class RoundCountsMergedMode
{
    private const string Mode = "--round-counts-merged";

    /// <summary>At most this many snapshot paths: each is read from a possibly slow share inside the extension's one spawn deadline.</summary>
    internal const int MaxPaths = 32;

    internal static readonly string Usage =
        $"{Mode} takes `--now <ISO-8601 UTC instant>` (such as --now 2026-10-10T12:00:00Z) at most once and any number of ABSOLUTE snapshot paths (at most {MaxPaths}), and nothing else";

    /// <summary>The instant, and what is left of the arguments once `--now` and its value are taken out.</summary>
    private sealed record Split(DateTime NowUtc, string[] Rest);

    /// <summary>The instant, and the snapshot paths in the order given, each once.</summary>
    internal sealed record Request(DateTime NowUtc, IReadOnlyList<string> Paths);

    /// <param name="env">The environment the data directory is resolved from, exactly as <c>--round-counts</c> resolves it.</param>
    /// <param name="output">stdout — the answer, and nothing else ever.</param>
    /// <param name="note">stderr — a refusal's sentence, through the channel this binary already prefixes.</param>
    /// <param name="clock">The clock a bare request ends its windows at; <c>--now</c> replaces it.</param>
    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note, TimeProvider clock)
    {
        var request = RequestOf(args, clock);
        if (!request.Ok)
        {
            note(request.Why);

            // 65, NOT 64: the binary HAS this mode and is refusing the request.
            return 65; // EX_DATAERR
        }

        return Answer(request.Value, env, output, note);
    }

    private static int Answer(Request request, Func<string, string?> env, TextWriter output, Action<string> note)
    {
        try
        {
            var dataDir = PanelSettings.FromEnvironment(env).DataDir;
            var answer = RoundCountsMerged.Read(dataDir, RoundCountsSnapshot.StoreOf(dataDir, env), Program.VersionText, request.Paths, request.NowUtc);
            output.WriteLine(JsonSerializer.Serialize(answer, ServerJsonContext.Default.RoundCountsMergedAnswer));

            return 0;
        }
        catch (Exception e) when (Program.Unreadable(e))
        {
            // This side's database is the disk, not the request and not an empty count: `--round-counts` answers it with the same code and sentence.
            note(Program.WhyUnreadable(e));

            return 74; // EX_IOERR
        }
    }

    /// <summary>
    /// The request, or the sentence that refuses it. Anything unexpected is refused, never ignored: a flag silently dropped would be a count about
    /// the wrong request.
    /// </summary>
    internal static Outcome<Request> RequestOf(string[] args, TimeProvider clock) =>
        NowAndRest(args[1..], clock).Then(split => PathsOf(split.Rest).Then(paths => Outcome<Request>.Of(new Request(split.NowUtc, paths))));

    private static Outcome<Split> NowAndRest(string[] tokens, TimeProvider clock) =>
        tokens.Count(IsNow) switch
        {
            0 => Outcome<Split>.Of(new Split(clock.GetUtcNow().UtcDateTime, tokens)),
            1 => OneNow(tokens),
            _ => Outcome<Split>.Refuse($"{Usage} — `--now` was given more than once"),
        };

    private static bool IsNow(string token) => token == "--now";

    private static Outcome<Split> OneNow(string[] tokens)
    {
        var at = Array.IndexOf(tokens, "--now");

        return at + 1 >= tokens.Length
            ? Outcome<Split>.Refuse($"{Usage} — `--now` needs a value")
            : Instant(tokens[at + 1]).Then(now => Outcome<Split>.Of(new Split(now, [.. tokens[..at], .. tokens[(at + 2)..]])));
    }

    private static Outcome<DateTime> Instant(string text) =>
        Program.TryParseInstant(text, out var at)
            ? Outcome<DateTime>.Of(at.UtcDateTime)
            : Outcome<DateTime>.Refuse($"{Mode} --now needs an ISO-8601 UTC instant, such as 2026-10-10T12:00:00Z; '{text}' is not one");

    /// <summary>The cap is on the arguments GIVEN, counted before anything is deduplicated or copied: a request of ten thousand paths is refused at its length.</summary>
    private static Outcome<IReadOnlyList<string>> PathsOf(string[] rest) =>
        rest.FirstOrDefault(token => token.StartsWith("--", StringComparison.Ordinal)) is { } flag
            ? Outcome<IReadOnlyList<string>>.Refuse($"{Usage} — '{flag}' is not a flag it knows")
            : rest.Length > MaxPaths
                ? Outcome<IReadOnlyList<string>>.Refuse($"{Usage} — {rest.Length} snapshot paths were given; at most {MaxPaths} are read")
                : Absolute([.. rest.Distinct(StringComparer.Ordinal)]);

    private static Outcome<IReadOnlyList<string>> Absolute(IReadOnlyList<string> paths) =>
        paths.FirstOrDefault(path => !Path.IsPathFullyQualified(path)) is { } relative
            ? Outcome<IReadOnlyList<string>>.Refuse($"{Usage} — '{relative}' is not an absolute path")
            : Outcome<IReadOnlyList<string>>.Of(paths);
}
