using CoaiBench.Model;

namespace CoaiBench.Cli;

/// <summary>What to measure, as a person types it.</summary>
/// <remarks>
/// <para>Every verb here exists because the same run had to be written by hand at least twice: all
/// models three times each, one model on its own, the same model local against hosted, five windows
/// at once, plans only, diffs only, both. The point of the project is that none of them is written
/// again.</para>
/// <para><b>Nothing runs that was not named.</b> There is no default vendor and no default model:
/// a bench that picks vendors for you spends somebody's quota on a guess.</para>
/// </remarks>
public sealed record Options
{
    public string Verb { get; init; } = "run";

    /// <summary>
    /// The `coai-mcp` to drive. Defaults to the INSTALLED one — the binary the panel spawns.
    /// </summary>
    /// <remarks>
    /// Measuring a build nobody has installed is measuring the wrong thing, and remembering the path
    /// every time is how the wrong one gets measured.
    /// </remarks>
    public string Executable { get; init; } = string.Empty;

    /// <summary>The checkout the cases live in and the rounds review.</summary>
    public string Repo { get; init; } = string.Empty;

    /// <summary>Which cases, by name, or empty for every case in the corpus.</summary>
    public IReadOnlyList<string> Cases { get; init; } = [];

    public string CorpusFile { get; init; } = string.Empty;

    /// <summary>Where the vendors are read from. Default: the panel's own settings file.</summary>
    public string VendorsFile { get; init; } = string.Empty;

    public Stages Stages { get; init; } = Stages.Both;

    /// <summary>One arm per vendor set. `codex,gemini,local` is ONE arm of three vendors.</summary>
    public IReadOnlyList<string> Arms { get; init; } = [];

    /// <summary>Model per vendor, as `vendor=model`, applied to every arm that has that vendor.</summary>
    public IReadOnlyDictionary<string, string> Models { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times each cell runs. Two is the floor for believing anything.</summary>
    public int Repeat { get; init; } = 1;

    /// <summary>
    /// How many runs are in flight at once — each in its own server process. 0 means "as many as
    /// there are arms", which is the default and the point.
    /// </summary>
    /// <remarks>
    /// <para><b>Independent runs overlap by default.</b> Comparing three vendors is three arms that
    /// have nothing to say to each other, and running them one after another triples the evening for
    /// no reason. Asked for explicitly, after the third matrix that was run in series.</para>
    /// <para>Set it to a number and it is also the "five windows" case — five servers sharing one
    /// data directory and one GPU, which is the only faithful way to have that. Inside a single
    /// round the vendors already fan out on their own, bounded by the server's own limits; a local
    /// vendor is serialised behind the engine lease however many lanes there are.</para>
    /// </remarks>
    public int Parallel { get; init; }

    /// <summary>
    /// Whether each run gets a data directory of its own instead of the real one.
    /// </summary>
    /// <remarks>
    /// OFF by default, and that is deliberate: with the real directory the rounds appear in the
    /// panel's *Recent rounds* as they happen, which is where a person watches a campaign — and it
    /// is also what a window actually does. Isolation is for comparing two configurations that must
    /// not see each other; it is a choice, not the normal case.
    /// </remarks>
    public bool Isolate { get; init; }

    /// <summary>Settings handed to every server, as `COAI_X=value`.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Where the run files and the tables go.</summary>
    public string OutDir { get; init; } = string.Empty;

    /// <summary>`judge` only: which model reads the findings and says which were worth having.</summary>
    public string Judge { get; init; } = "claude-fable-5-1";

    /// <summary>`judge`/`table` only: the run file to work on.</summary>
    public string RunsFile { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
}

public static class OptionsParser
{
    /// <summary>
    /// The command line, or a sentence saying what is wrong with it.
    /// </summary>
    /// <remarks>
    /// Refusing early and by name is the whole of the input validation here: a bench that starts and
    /// then discovers it has no repository has already spent a minute of somebody's evening.
    /// </remarks>
    public static (Options? Options, string Refusal) Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return (null, Usage);
        }

        var options = new Options { Verb = args[0] };
        var models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var at = 1; at < args.Count; at++)
        {
            // A switch takes no value, and must not swallow the flag after it — which is what a
            // parser that demands one for everything does.
            if (Switches.Contains(args[at]))
            {
                options = ApplySwitch(options, args[at]);
                continue;
            }

            var (name, value) = Flag(args, ref at);
            if (value.Length == 0)
            {
                return (null, $"'{name}' needs a value");
            }

            options = Apply(options, name, value, models, settings);
            if (options.Verb.Length == 0)
            {
                return (null, $"unknown option '{name}'\n\n{Usage}");
            }
        }

        return (options with { Models = models, Settings = settings }, string.Empty);
    }

    /// <summary>Flags that are on or off, and therefore take nothing after them.</summary>
    private static readonly HashSet<string> Switches = new(StringComparer.Ordinal) { "--isolate" };

    private static Options ApplySwitch(Options options, string name) =>
        name == "--isolate" ? options with { Isolate = true } : options;

    private static (string Name, string Value) Flag(IReadOnlyList<string> args, ref int at)
    {
        var name = args[at];
        var split = name.IndexOf('=', StringComparison.Ordinal);
        if (split > 0)
        {
            return (name[..split], name[(split + 1)..]);
        }

        var value = at + 1 < args.Count ? args[at + 1] : string.Empty;
        at++;

        return (name, value);
    }

    /// <summary>An unknown flag blanks the verb, which is how the caller learns it was unknown.</summary>
    private static Options Apply(
        Options options,
        string name,
        string value,
        Dictionary<string, string> models,
        Dictionary<string, string> settings)
    {
        switch (name)
        {
            case "--exe": return options with { Executable = value };
            case "--repo": return options with { Repo = value };
            case "--case": return options with { Cases = [.. options.Cases, .. Split(value)] };
            case "--corpus": return options with { CorpusFile = value };
            case "--vendors-from": return options with { VendorsFile = value };
            case "--stages": return options with { Stages = StagesFrom(value) };
            case "--arm": return options with { Arms = [.. options.Arms, value] };
            case "--repeat": return options with { Repeat = Number(value, options.Repeat) };
            case "--parallel": return options with { Parallel = Number(value, options.Parallel) };
            case "--out": return options with { OutDir = value };
            case "--judge": return options with { Judge = value };
            case "--runs": return options with { RunsFile = value };
            case "--timeout-minutes":
                return options with { Timeout = TimeSpan.FromMinutes(Number(value, 30)) };
            case "--model": return Remember(options, models, value);
            case "--set": return Remember(options, settings, value);
            default: return options with { Verb = string.Empty };
        }
    }

    private static Options Remember(Options options, Dictionary<string, string> into, string pair)
    {
        var split = pair.IndexOf('=', StringComparison.Ordinal);
        if (split <= 0)
        {
            return options with { Verb = string.Empty };
        }

        into[pair[..split]] = pair[(split + 1)..];

        return options;
    }

    private static IReadOnlyList<string> Split(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static int Number(string value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static Stages StagesFrom(string value) => value.ToLowerInvariant() switch
    {
        "plans" => Stages.Plans,
        "diffs" => Stages.Diffs,
        _ => Stages.Both,
    };

    public const string Usage = """
        coai-bench — measure the gate, and keep the measurement

          run     drive real rounds and record everything
          judge   read a recorded run and mark which findings were worth having
          table   print the tables again from a recorded run

        run:
          --exe <coai-mcp>          the server to drive (the published one)
          --repo <path>             the checkout the cases live in
          --corpus <file>           plan+commit pairs, JSON
          --vendors-from <file>     the settings file the vendors come from. Default: the
                                    panel's own, so an arm names IDS and their runtime and
                                    model are the ones actually configured
          --case <name[,name]>      only these cases; default is all of them
          --stages plans|diffs|both default both
          --arm <codex,gemini>      one arm per vendor SET; repeatable
          --model <vendor>=<model>  the model that vendor runs
          --set COAI_X=<value>      a setting handed to every server
          --repeat <n>              runs per cell; two is the floor for believing anything
          --parallel <n>            runs in flight at once, each its own server. Default: one lane
                                    per ARM, so a three-vendor matrix takes a third of the evening.
                                    Give it a number and it is the "five windows" case as well
          --out <dir>               where the runs and tables go
          --timeout-minutes <n>     per run

        judge:
          --runs <file>             the run file to mark up
          --judge <model>           the model that reads the findings (default claude-fable-5-1)
          --repo <path>             the checkout the findings name

        Nothing runs that was not named: there is no default vendor and no default model.
        """;
}
