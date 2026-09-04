using CoaiBench.Cli;
using CoaiBench.Judging;
using CoaiBench.Model;
using CoaiBench.Reporting;
using CoaiBench.Running;
using CoaiBench.Store;

namespace CoaiBench;

/// <summary>
/// coai-bench — the measuring instrument, kept.
/// </summary>
/// <remarks>
/// <para>Every run this project performs had been written by hand at least twice as a throwaway
/// script: all models three times each, one model alone, the same model local against hosted, five
/// windows at once, plans only, diffs only, both. Each rewrite measured something slightly different
/// from the last, which is the real cost — not the typing.</para>
/// <para>It records and does not judge. What was WORTH having is a second pass, over data already on
/// disk, so a change of mind about worth costs a judgement and not another evening of rounds.</para>
/// </remarks>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Flushed per line. A campaign runs for an hour and .NET buffers a redirected stdout, so
        // every line arrived at the end — which for a long measurement means watching nothing happen
        // and being unable to tell a slow round from a hung one.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        var (options, refusal) = OptionsParser.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine(refusal);

            return 64; // EX_USAGE
        }

        using var stopping = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };

        return options.Verb switch
        {
            "run" => await RunAsync(options, stopping.Token),
            "judge" => await JudgeAsync(options, stopping.Token),
            "table" => await TableAsync(options, stopping.Token),
            _ => Refuse($"unknown verb '{options.Verb}'\n\n{OptionsParser.Usage}"),
        };
    }

    private static int Refuse(string why)
    {
        Console.Error.WriteLine(why);

        return 64;
    }

    private static async Task<int> RunAsync(Options options, CancellationToken ct)
    {
        if (Missing(options) is { Length: > 0 } missing)
        {
            return Refuse(missing);
        }

        var corpus = await CorpusAsync(options, ct);
        if (corpus.Count == 0)
        {
            return Refuse("no cases to run — the corpus is empty or the names matched nothing");
        }

        // The operator's OWN vendors. An id is not a vendor: the runtime and the model are, and a
        // bench that rebuilds them from names measures a machine nobody has — it ran the retired
        // Gemini CLI under a vendor configured for antigravity, and a local one with no model.
        var vendorsFile = options.VendorsFile.Length > 0 ? options.VendorsFile : Vendors.DefaultSettingsFile;
        var configured = Vendors.Read(vendorsFile);
        if (configured.Count == 0)
        {
            return Refuse($"no vendors are configured in '{vendorsFile}' — point --vendors-from at "
                + "the settings file the panel writes, or configure a reviewer in the panel first");
        }

        // Every setting the operator has, then this run's overrides — and both are PRINTED and kept
        // with the run. A campaign has to be able to say what it was measuring months later, without
        // anybody remembering; and taking only the vendors left thresholds, rounds per role, prompts
        // and the exhausted-policy at the server's defaults, describing a machine nobody runs.
        var settings = PanelSettingsFile.Effective(PanelSettingsFile.Read(vendorsFile), options.Settings);
        Console.WriteLine(
            $"vendors from {vendorsFile}: "
            + string.Join(", ", configured.Select(v => $"{v.Id} ({v.Runtime}/{v.Model})")));
        Console.WriteLine($"settings in force:\n{PanelSettingsFile.Describe(settings)}\n");
        var outDir = OutDir(options);
        var runs = await new Bench(
            options with { OutDir = outDir, Executable = WhichServer(options) },
            corpus, configured, settings, Console.WriteLine)
            .RunAsync(ct);
        await File.WriteAllTextAsync(
            Path.Combine(outDir, "settings.md"),
            $"# The settings this campaign ran under\n\nFrom `{vendorsFile}`, with `--set` on top.\n\n"
                + $"```\n{PanelSettingsFile.Describe(settings)}\n```\n",
            ct);
        var file = Path.Combine(outDir, "runs.json");
        await RunStore.SaveAsync(file, runs, ct);
        Report(runs, outDir);
        Console.WriteLine($"\n{runs.Count} run(s) → {file}");

        return runs.Any(r => r.Stages.Count == 0) ? 1 : 0;
    }

    /// <summary>What the run cannot start without, said all at once rather than one flag at a time.</summary>
    private static string Missing(Options options)
    {
        var wrong = new List<string>();
        if (!File.Exists(WhichServer(options)))
        {
            wrong.Add($"no coai-mcp at '{WhichServer(options)}' — install the extension, or name one with --exe");
        }

        if (options.Repo.Length == 0 || !Directory.Exists(options.Repo))
        {
            wrong.Add($"--repo must name a checkout (got '{options.Repo}')");
        }

        if (options.Arms.Count == 0)
        {
            wrong.Add("--arm names the vendors to run; there is no default, because a default spends "
                + "somebody's quota on a guess");
        }

        return string.Join("\n", wrong);
    }

    /// <summary>
    /// The server to drive: the one named, or the INSTALLED one.
    /// </summary>
    /// <remarks>
    /// Measuring a build nobody has installed is measuring the wrong thing, and having to remember
    /// the path every time is how the wrong one gets measured.
    /// </remarks>
    internal static string WhichServer(Options options) =>
        options.Executable.Length > 0 ? options.Executable : InstalledServer;

    internal static string InstalledServer => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Code", "User", "globalStorage", "remsoftdev.connect-other-ais",
        OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp");

    private static async Task<IReadOnlyList<Case>> CorpusAsync(Options options, CancellationToken ct)
    {
        var all = options.CorpusFile.Length > 0
            ? await RunStore.LoadCorpusAsync(options.CorpusFile, ct)
            : [];

        return options.Cases.Count == 0
            ? all
            : [.. all.Where(c => options.Cases.Contains(c.Name, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Where the run goes — ABSOLUTE, because the server is handed it as its data directory.
    /// </summary>
    /// <remarks>
    /// A relative one reached the server on the first real run and every reviewer failed with
    /// "cannot find the path specified": a vendor CLI launches in a directory of its own. The server
    /// resolves it now as well, and both are worth having — this one so the run files land where the
    /// person expected, that one so no other caller can make the same mistake.
    /// </remarks>
    private static string OutDir(Options options) =>
        Path.GetFullPath(options.OutDir.Length > 0
            ? options.OutDir
            : Path.Combine("artifacts", "bench", DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss")));

    private static async Task<int> JudgeAsync(Options options, CancellationToken ct)
    {
        if (options.RunsFile.Length == 0 || !File.Exists(options.RunsFile))
        {
            return Refuse("--runs must name a run file to mark up");
        }

        var runs = await RunStore.LoadAsync(options.RunsFile, ct);
        var judge = new Judge("claude", options.Judge, options.Repo);
        var judged = new List<RunRecord>();
        foreach (var run in runs)
        {
            judged.Add(await judge.JudgeAsync(run, ct));
            var counted = judged[^1].Stages.SelectMany(s => s.Findings).ToList();
            Console.WriteLine(
                $"{run.Arm,-22} {run.Case.Name,-34} #{run.Repeat} "
                + $"{counted.Count(f => f.Useful == "yes")}/{counted.Count} worth having");
        }

        await RunStore.SaveAsync(options.RunsFile, judged, ct);
        Report(judged, Path.GetDirectoryName(options.RunsFile) ?? ".");

        return 0;
    }

    private static async Task<int> TableAsync(Options options, CancellationToken ct)
    {
        if (options.RunsFile.Length == 0 || !File.Exists(options.RunsFile))
        {
            return Refuse("--runs must name a run file to read");
        }

        var runs = await RunStore.LoadAsync(options.RunsFile, ct);
        Report(runs, Path.GetDirectoryName(options.RunsFile) ?? ".");

        return 0;
    }

    private static void Report(IReadOnlyList<RunRecord> runs, string outDir)
    {
        var perArm = Tables.PerArm(runs);
        var perRun = Tables.PerRun(runs);
        Console.WriteLine($"\n{perArm}\n{perRun}");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(
            Path.Combine(outDir, "tables.md"),
            $"# Bench\n\n## Per arm\n\n{perArm}\n## Per run\n\n{perRun}");
    }
}
