using System.Globalization;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--round-authors [--since &lt;utc&gt;] [--until &lt;utc&gt;] [--split &lt;utc&gt;] [--stage code|plan] [--cohort first|corrective|all]
/// [--with-before-open] [--kinds &lt;k,…&gt;] [--without-elided] [--with-uncovered] [--add &lt;file.json&gt;]… [--table]</c>: is the code better or
/// worse since the coders moved to a cheaper model? — the answer from the rounds database, read-only (E6.S5 of
/// <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md</c>).
/// </summary>
/// <remarks>
/// <para><b>The contract.</b> By default stdout is ONE JSON document of SUMS (<see cref="RoundAuthorsAnswer"/>, <c>format</c> 1: the selection, the
/// sources, the cells, the exclusions — integers, sets and counts, no figure, no mark, no header, no absolute path). <c>--table</c> prints the
/// report: the header, a table of cells per cohort and period, and the exclusions with every reason listed. <c>--add &lt;file&gt;</c> (repeatable)
/// adds the JSON a previous run printed — a WSL run saved to a file plus the Windows run print the combined report without opening a store across
/// the drvfs boundary; two reports add only when they are one population (same selection, no shared gate session).</para>
/// <para><b>Exit codes.</b> <b>0</b> an answer — an EMPTY one when no database exists; <b>65</b> a bad request: an unknown or repeated flag, an
/// unparseable instant, an <c>--add</c> that cannot be read, parsed or added; <b>74</b> a database that will not read. <b>Never 64</b>: that is
/// "this binary has never heard of the mode", how a client spots an older server (<c>.agents/PROJECT.md</c>).</para>
/// <para><b>Nothing is written.</b> The read is <see cref="RoundAuthorsQuery"/>'s read-only open; a reader never migrates, and a missing file is
/// never created.</para>
/// </remarks>
internal static class RoundAuthorsMode
{
    private const string Mode = "--round-authors";

    private const string Epoch = "1970-01-01T00:00:00.0000000Z";

    private static readonly string[] AllKinds = ["code", "config", "docs", "test", "unknown"];

    private static readonly string[] ValueFlags = ["--since", "--until", "--split", "--stage", "--cohort", "--kinds", "--add"];

    private static readonly string[] SwitchFlags = ["--with-before-open", "--without-elided", "--with-uncovered", "--table"];

    /// <summary>What a request asks for, once it parsed: the selection, the files to add, the rendering and the instant the read ends at.</summary>
    internal sealed record Request(RoundAuthorsSelection Selection, IReadOnlyList<string> Adds, bool Table, string ReadUtc);

    private readonly record struct Step<T>(bool Ok, T Value, string Why)
    {
        public static Step<T> Of(T value) => new(true, value, string.Empty);

        public static Step<T> Refuse(string why) => new(false, default!, why);

        public Step<TNext> Then<TNext>(Func<T, Step<TNext>> next) => Ok ? next(Value) : Step<TNext>.Refuse(Why);
    }

    private sealed record Flags(IReadOnlyDictionary<string, IReadOnlyList<string>> Values, IReadOnlySet<string> Switches)
    {
        public string One(string flag) => Values.TryGetValue(flag, out var found) ? found[0] : string.Empty;

        public bool Has(string flag) => Values.ContainsKey(flag) || Switches.Contains(flag);
    }

    private sealed record Window(string Since, string Until, string Split, string ReadUtc);

    /// <param name="env">The environment the data directory is resolved from, exactly as <c>--round-counts</c> resolves it.</param>
    /// <param name="output">stdout — the answer, and nothing else ever.</param>
    /// <param name="note">stderr — a refusal's sentence, through the channel this binary already prefixes.</param>
    /// <param name="clock">Where an unset <c>--until</c> ends the window.</param>
    /// <param name="readFile">Reads an <c>--add</c> file as text; <c>File.ReadAllText</c> in production, which honours a byte-order mark.</param>
    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note, TimeProvider clock, Func<string, string> readFile)
    {
        var parsed = Parse(args, clock);
        if (!parsed.Ok)
        {
            // 65, NOT 64: the binary HAS this mode and is refusing the request.
            note(parsed.Why);

            return 65; // EX_DATAERR
        }

        return Answer(parsed.Value, PanelSettings.FromEnvironment(env).DataDir, output, note, readFile);
    }

    private static int Answer(Request request, string dataDir, TextWriter output, Action<string> note, Func<string, string> readFile)
    {
        try
        {
            var local = RoundAuthorsMerge.Report(dataDir, request.Selection, request.ReadUtc);

            return Combined(local, request, output, note, readFile);
        }
        catch (Exception e) when (Program.Unreadable(e))
        {
            // A database that will not read is the disk, not the request, and not an empty report.
            note(Program.WhyUnreadable(e));

            return 74; // EX_IOERR
        }
    }

    /// <summary>The local report with every <c>--add</c> added in argument order; the first that cannot be read, parsed or added ends the run with 65.</summary>
    private static int Combined(RoundAuthorsAnswer local, Request request, TextWriter output, Action<string> note, Func<string, string> readFile)
    {
        var answer = local;
        foreach (var path in request.Adds)
        {
            var added = Added(answer, path, readFile);
            if (!added.Ok)
            {
                note($"{Mode}: --add '{path}': {added.Why}");

                return 65;
            }

            answer = added.Answer;
        }

        output.WriteLine(request.Table ? RoundAuthorsText.Render(answer).TrimEnd('\n') : RoundAuthorsMerge.Serialize(answer));

        return 0;
    }

    private static MergeOutcome Added(RoundAuthorsAnswer mine, string path, Func<string, string> readFile)
    {
        try
        {
            var parsed = RoundAuthorsMerge.Parse(readFile(path));

            return parsed.Ok ? RoundAuthorsMerge.Add(mine, parsed.Answer) : parsed;
        }
        catch (Exception e) when (IsNoReadableFile(e))
        {
            return new MergeOutcome(false, mine, "it could not be read: " + e.Message);
        }
    }

    /// <summary>What reading an <c>--add</c> argument throws when it names no readable file — an empty or malformed path included, which is a bad request (65), never a crash.</summary>
    private static bool IsNoReadableFile(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    /// <summary>The request, or the refusal sentence — pure over the arguments and the clock.</summary>
    internal static (bool Ok, Request Value, string Why) Parse(string[] args, TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var step = Tokens(args).Then(flags => WindowOf(flags, now).Then(window => Rest(flags, window)));

        return (step.Ok, step.Value, step.Why);
    }

    private static Step<Flags> Tokens(string[] args)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);
        for (var at = 1; at < args.Length; at++)
        {
            var problem = Take(args, ref at, values, switches);
            if (problem.Length > 0)
            {
                return Step<Flags>.Refuse(problem);
            }
        }

        return Step<Flags>.Of(new Flags(values, switches));
    }

    /// <summary>One flag (and its value) taken from the arguments; the refusal sentence, or empty when it was fine.</summary>
    private static string Take(string[] args, ref int at, Dictionary<string, IReadOnlyList<string>> values, HashSet<string> switches)
    {
        var flag = args[at];
        if (SwitchFlags.Contains(flag))
        {
            return switches.Add(flag) ? string.Empty : Refusal(flag, string.Empty, "given twice");
        }

        return ValueFlags.Contains(flag) ? TakeValue(args, ref at, values) : Refusal(flag, flag, "is not a flag of this mode");
    }

    private static string TakeValue(string[] args, ref int at, Dictionary<string, IReadOnlyList<string>> values)
    {
        var flag = args[at];
        if (at + 1 >= args.Length)
        {
            return Refusal(flag, string.Empty, "needs a value");
        }

        at++;

        return Store(values, flag, args[at]);
    }

    /// <summary>Every flag at most once except <c>--add</c>, which is repeatable and keeps its order.</summary>
    private static string Store(Dictionary<string, IReadOnlyList<string>> values, string flag, string value)
    {
        values.TryGetValue(flag, out var earlier);
        if (earlier is not null && flag != "--add")
        {
            return Refusal(flag, value, "given twice");
        }

        values[flag] = [.. earlier ?? [], value];

        return string.Empty;
    }

    private static string Refusal(string flag, string value, string what) =>
        value.Length == 0 ? $"{Mode}: `{flag}` {what}" : $"{Mode}: `{flag}` '{value}' {what}";

    private static Step<Window> WindowOf(Flags flags, string now)
    {
        var since = Instant(flags, "--since", Epoch);
        var until = Instant(flags, "--until", string.Empty);
        var split = Instant(flags, "--split", string.Empty);
        var problem = new[] { since, until, split }.FirstOrDefault(step => !step.Ok);
        if (problem.Why is { Length: > 0 })
        {
            return Step<Window>.Refuse(problem.Why);
        }

        return OrderedWindow(new Window(since.Value, until.Value, split.Value, now));
    }

    private static Step<string> Instant(Flags flags, string flag, string fallback)
    {
        var text = flags.One(flag);

        return text.Length == 0 ? Step<string>.Of(fallback)
            : Program.TryParseInstant(text, out var at) ? Step<string>.Of(at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            : Step<string>.Refuse(Refusal(flag, text, "is not an ISO-8601 UTC instant, such as 2026-10-10T12:00:00Z"));
    }

    /// <summary><c>--until</c> after <c>--since</c>, and <c>--split</c> inside <c>[since, until)</c>: compared textually over the column's own UTC stamps.</summary>
    private static Step<Window> OrderedWindow(Window window)
    {
        var until = window.Until.Length == 0 ? window.ReadUtc : window.Until;

        return string.CompareOrdinal(until, window.Since) <= 0
            ? Step<Window>.Refuse($"{Mode}: `--until` '{until}' is not after `--since` '{window.Since}'")
            : SplitInside(window, until);
    }

    private static Step<Window> SplitInside(Window window, string until) =>
        window.Split.Length > 0 && !Inside(window.Split, window.Since, until)
            ? Step<Window>.Refuse($"{Mode}: `--split` '{window.Split}' is outside the window [{window.Since}, {until})")
            : Step<Window>.Of(window);

    private static bool Inside(string at, string since, string until) =>
        string.CompareOrdinal(at, since) >= 0 && string.CompareOrdinal(at, until) < 0;

    private static Step<Request> Rest(Flags flags, Window window)
    {
        var stage = Choice(flags, "--stage", "code", ["code", "plan"]);
        var cohort = Choice(flags, "--cohort", Cohort.First, [Cohort.First, Cohort.Corrective, Cohort.All]);
        var kinds = Kinds(flags);
        var problem = new[] { stage.Why, cohort.Why, kinds.Why }.FirstOrDefault(why => why.Length > 0);
        if (problem is { Length: > 0 })
        {
            return Step<Request>.Refuse(problem);
        }

        return NoDiffFlags(flags, stage.Value) is { Length: > 0 } conflict
            ? Step<Request>.Refuse(conflict)
            : Step<Request>.Of(Compose(flags, window, stage.Value, cohort.Value, kinds.Value));
    }

    private static Request Compose(Flags flags, Window window, string stage, string cohort, string[] kinds) => new(
        new RoundAuthorsSelection(
            stage == "plan" ? nameof(Stage.PlanReview) : nameof(Stage.CodeReview),
            window.Since,
            window.Until,
            window.Split,
            cohort,
            kinds,
            flags.Has("--with-before-open"),
            flags.Has("--without-elided"),
            flags.Has("--with-uncovered")),
        flags.Values.TryGetValue("--add", out var adds) ? adds : [],
        flags.Has("--table"),
        window.ReadUtc);

    /// <summary>A plan has no diff: the flags that are about one are refused rather than ignored.</summary>
    private static string NoDiffFlags(Flags flags, string stage) =>
        stage == "plan" ? PlanRefusal(flags) : string.Empty;

    private static string PlanRefusal(Flags flags) =>
        flags.Has("--kinds") ? Refusal("--kinds", flags.One("--kinds"), "needs the code stage: a plan has no diff")
        : flags.Has("--without-elided") ? Refusal("--without-elided", string.Empty, "needs the code stage: a plan has no diff")
        : string.Empty;

    private static (string Value, string Why) Choice(Flags flags, string flag, string fallback, string[] allowed)
    {
        var text = flags.Has(flag) ? flags.One(flag) : fallback;

        return allowed.Contains(text) ? (text, string.Empty) : (string.Empty, Refusal(flag, text, $"is not one of {string.Join(", ", allowed)}"));
    }

    /// <summary>The kinds counted: a non-empty subset of the five, sorted and made distinct HERE so two spellings of one request are one selection.</summary>
    private static (string[] Value, string Why) Kinds(Flags flags)
    {
        var asked = flags.Has("--kinds") ? flags.One("--kinds").Split(',') : ["code", "test"];
        var unknown = asked.FirstOrDefault(kind => !AllKinds.Contains(kind));

        return unknown is not null
            ? ([], Refusal("--kinds", unknown, $"is not one of {string.Join(", ", AllKinds)}"))
            : ([.. asked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], string.Empty);
    }
}
