using CoaiBench.Cli;
using CoaiBench.Model;

namespace CoaiBench.Running;

/// <summary>One cell of the matrix: a case, an arm, and which repetition this is.</summary>
public sealed record Cell(Case Case, string Arm, int Repeat);

/// <summary>
/// The matrix, run.
/// </summary>
/// <remarks>
/// <para>Every cell is independent, so they overlap: comparing three vendors is three arms with
/// nothing to say to each other, and running them in series triples the evening for no reason. The
/// lane count defaults to the number of ARMS and is a plain number when the five-window case is what
/// is wanted.</para>
/// <para>Each cell gets a server of its own. That is not tidiness — a session is per repo+branch and
/// a data directory is shared, and both of those are things this bench is often measuring.</para>
/// </remarks>
public sealed class Bench(
    Options options,
    IReadOnlyList<Case> corpus,
    IReadOnlyList<VendorConfig> configured,
    IReadOnlyDictionary<string, string> settings,
    Action<string> say)
{
    public IReadOnlyList<Cell> Cells() =>
    [
        .. from work in corpus
           from arm in options.Arms
           from repeat in Enumerable.Range(1, options.Repeat)
           select new Cell(work, arm, repeat),
    ];

    /// <summary>Lanes: as many as there are arms unless a number was given.</summary>
    public int Lanes() => options.Parallel > 0 ? options.Parallel : Math.Max(1, options.Arms.Count);

    public async Task<IReadOnlyList<RunRecord>> RunAsync(CancellationToken ct)
    {
        var cells = Cells();
        var done = new RunRecord[cells.Count];
        var next = -1;
        var lanes = Math.Min(Lanes(), cells.Count);
        say($"{cells.Count} run(s) over {options.Arms.Count} arm(s), {lanes} in flight");

        await Task.WhenAll(Enumerable.Range(1, lanes).Select(lane => Task.Run(async () =>
        {
            for (var at = Interlocked.Increment(ref next); at < cells.Count;
                 at = Interlocked.Increment(ref next))
            {
                done[at] = await OneAsync(cells[at], lane, ct);
                say(Line(done[at]));
            }
        }, ct)));

        return done;
    }

    private async Task<RunRecord> OneAsync(Cell cell, int lane, CancellationToken ct)
    {
        // The operator's OWN vendors, selected by id — never a list rebuilt from names. Bare ids ran
        // the retired Gemini CLI under a vendor whose configured runtime is `antigravity`, and a
        // local vendor with no model at all, and the report blamed the release for both.
        var (vendors, refusal) = Vendors.For(cell.Arm, configured, options.Models);
        if (refusal.Length > 0)
        {
            return new RunRecord(cell.Case, cell.Arm, cell.Repeat, lane) { HarnessError = refusal };
        }

        // The operator's WHOLE configuration, then this arm's vendors, then this run's
        // overrides. Taking only the vendors left thresholds, rounds per role, prompts and the
        // exhausted-policy at the server's defaults, so every number described a machine nobody runs.
        var env = new Dictionary<string, string>(settings, StringComparer.Ordinal)
        {
            ["COAI_PROVIDERS"] = string.Join(",", vendors.Select(v => v.Id)),
            ["COAI_VENDORS"] = Vendors.AsSetting(vendors),
        };

        var dataDir = DataDirFor(cell, lane);
        // A stale session is the previous campaign, not this one: it remembers the configuration it
        // was opened with and how far its stages got. One cost a whole run — every round came out on
        // the DEFAULT rounds and thresholds while the operator had set their own.
        Sessions.Reset(dataDir, options.Repo, BranchFor(cell.Case));
        await using var client = new GateClient(options.Executable, dataDir, env);
        var run = await new RoundRunner(client, options.Repo, options.Timeout)
            .RunAsync(cell.Case, cell.Arm, cell.Repeat, lane, options.Stages);

        // The disk, not the answer. They came apart once and nothing said so.
        return run with { OnDisk = OnDisk.Read(dataDir) };
    }

    /// <summary>
    /// A data directory per RUN by default, and one SHARED when the windows case is being measured.
    /// </summary>
    /// <remarks>
    /// The distinction is the measurement. Comparing two models wants them not to interfere; asking
    /// what five windows do to each other wants exactly the interference, and that lives in the
    /// shared directory — the sessions, the engine lease, the caller memory.
    /// </remarks>
    /// <summary>The branch a case is reviewed on — its commit, or the checkout as it stands.</summary>
    internal static string BranchFor(Case work) => work.Commit.Length > 0 ? work.Commit : "HEAD";

    internal string DataDirOf(Case work, string arm, int repeat, int lane) =>
        DataDirFor(new Cell(work, arm, repeat), lane);

    private string DataDirFor(Cell cell, int lane) =>
        !options.Isolate
            ? RealDataDir
            : options.Parallel > 0
                ? Path.Combine(options.OutDir, "data-shared")
                : Path.Combine(options.OutDir, "data", $"{cell.Arm}-{cell.Case.Name}-{cell.Repeat}-{lane}");

    /// <summary>The directory the panel reads — which is why the rounds appear in it.</summary>
    internal static string RealDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "coai-mcp");

    private static string Line(RunRecord run)
    {
        var stages = run.Stages.Count == 0
            ? $"NOTHING RAN {run.HarnessError} {run.ServerSaid}"
            : string.Join("  |  ", run.Stages.Select(s =>
                $"{s.Stage}: {(s.Verdict.Length > 0 ? s.Verdict : "ERROR " + s.Error)} "
                + $"{s.Findings.Count}f {s.Seconds}s {s.TokensIn}/{s.TokensOut}"));

        // The disk is printed beside the answer, because "12 findings" and "none of them can be
        // resolved" have looked identical from the answer alone.
        var disk = run.OnDisk is null || run.Stages.Count == 0
            ? string.Empty
            : run.OnDisk.Resolvable
                ? "  [resolvable]"
                : $"  [NOT RESOLVABLE: {run.OnDisk.StillRunning} still running, "
                    + $"{run.OnDisk.Pending} pending{Note(run.OnDisk.Note)}]";

        return $"{run.Arm,-22} {run.Case.Name,-34} #{run.Repeat} {stages}{disk}";
    }

    private static string Note(string note) => note.Length == 0 ? string.Empty : $", {note}";
}
