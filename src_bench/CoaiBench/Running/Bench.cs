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

    /// <summary>
    /// Runs the matrix, skipping what a previous attempt already finished.
    /// </summary>
    /// <param name="already">Runs recovered from an interrupted campaign, by <see cref="RunRecord.Key"/>.</param>
    /// <param name="finished">Called as each run lands, so an hour of work survives a Ctrl+C.</param>
    /// <remarks>
    /// A campaign is an hour of somebody's vendor quota. Losing it to an interrupted terminal, and
    /// paying for it twice, is not a thing a measuring instrument should make people risk — so every
    /// run is handed over the moment it completes, and a re-run of the same output directory picks up
    /// where the last one stopped.
    /// </remarks>
    public async Task<IReadOnlyList<RunRecord>> RunAsync(
        IReadOnlyList<RunRecord> already,
        Func<RunRecord, Task> finished,
        CancellationToken ct)
    {
        var recovered = already.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var cells = Cells();
        var todo = cells.Where(c => !recovered.ContainsKey(KeyOf(c))).ToList();
        var done = new RunRecord?[todo.Count];
        var next = -1;
        var lanes = Math.Max(1, Math.Min(Lanes(), todo.Count));
        say(recovered.Count > 0
            ? $"{cells.Count} run(s) over {options.Arms.Count} arm(s); {recovered.Count} already done, "
                + $"{todo.Count} to go, {lanes} in flight"
            : $"{cells.Count} run(s) over {options.Arms.Count} arm(s), {lanes} in flight");

        await Task.WhenAll(Enumerable.Range(1, lanes).Select(lane => Task.Run(async () =>
        {
            for (var at = Interlocked.Increment(ref next); at < todo.Count && !ct.IsCancellationRequested;
                 at = Interlocked.Increment(ref next))
            {
                var run = await OneAsync(todo[at], lane, ct);
                done[at] = run;
                say(Line(run));
                await finished(run);
            }
        }, CancellationToken.None)));

        // Recovered first, then whatever this attempt managed — in the matrix's own order, so a
        // resumed campaign reads exactly like one that never stopped.
        var fresh = done.Where(r => r is not null).ToDictionary(r => r!.Key, r => r!, StringComparer.Ordinal);

        return [.. cells
            .Select(c => recovered.GetValueOrDefault(KeyOf(c)) ?? fresh.GetValueOrDefault(KeyOf(c)))
            .Where(r => r is not null)
            .Select(r => r!)];
    }

    private static string KeyOf(Cell cell) => $"{cell.Arm}|{cell.Case.Name}|{cell.Repeat}";

    /// <summary>
    /// The branch a run is reviewed on: a ref OF ITS OWN at the case's commit, or the checkout as it
    /// stands for a plan-only case.
    /// </summary>
    /// <remarks>
    /// A session is keyed by repo+branch, and the branch must be a real ref because the server diffs
    /// it. This used to hand the case's commit as the branch, so three parallel repeats of one case
    /// shared one session key, one session file and one worktree name — and the five-window campaign
    /// of 2026-09-05 found it in its first minute: <c>fatal: 'coai-wt-a0fc7e7d-r1' already exists</c>,
    /// two servers creating the same worktree. The runner's own remark had promised a branch per run
    /// all along.
    /// </remarks>
    internal static string BranchFor(Cell cell) =>
        cell.Case.Commit.Length > 0 ? $"bench/{cell.Case.Name}-r{cell.Repeat}" : "HEAD";

    /// <summary>What the run's ref points at — the reviewed commit and nothing newer.</summary>
    internal static string RefTarget(Case work) => work.Commit;

    internal string DataDirOf(Case work, string arm, int repeat, int lane) =>
        DataDirFor(new Cell(work, arm, repeat), lane);

    /// <summary>
    /// The environment one cell's server runs under.
    /// </summary>
    /// <remarks>
    /// The operator's WHOLE configuration, then this arm's vendors, then this run's overrides.
    /// Taking only the vendors left thresholds, rounds per role, prompts and the exhausted-policy at
    /// the server's defaults, so every number described a machine nobody runs.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> EnvironmentFor(Cell cell)
    {
        var (vendors, _) = Vendors.For(cell.Arm, configured, options.Models);

        return new Dictionary<string, string>(settings, StringComparer.Ordinal)
        {
            ["COAI_PROVIDERS"] = string.Join(",", vendors.Select(v => v.Id)),
            ["COAI_VENDORS"] = Vendors.AsSetting(vendors),
            ["COAI_CALLER_SESSION"] = CallerFor(cell),
        };
    }

    /// <summary>
    /// Who this run says it is — one identity per run, because one run models one AI session.
    /// </summary>
    /// <remarks>
    /// <para>The gate gives the order to split a plan ONCE per calling session; that is the floor
    /// under epics-of-epics, and it is deliberate. A campaign that calls with one identity therefore
    /// measures the split path in its first run and the already-split path in every run after —
    /// which is exactly what happened on 2026-09-04, and three runs of four came back marked
    /// <c>SETTINGS NOT APPLIED</c> while the feature worked perfectly.</para>
    /// <para>The campaign stamp is taken once per process, so a second campaign over the same cells
    /// is a second set of sessions rather than yesterday's callers walking back in inside the
    /// gate's 24-hour memory. Within one process the identity of a cell never moves: a resumed
    /// campaign's recovered runs keep the identities they ran under.</para>
    /// </remarks>
    private string CallerFor(Cell cell) =>
        $"bench-{_campaign}-{cell.Arm}-{cell.Case.Name}-{cell.Repeat}";

    private readonly string _campaign =
        $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}";

    private async Task<RunRecord> OneAsync(Cell cell, int lane, CancellationToken ct)
    {
        // The operator's OWN vendors, selected by id — never a list rebuilt from names. Bare ids ran
        // the retired Gemini CLI under a vendor whose configured runtime is `antigravity`, and a
        // local vendor with no model at all, and the report blamed the release for both.
        var (_, refusal) = Vendors.For(cell.Arm, configured, options.Models);
        if (refusal.Length > 0)
        {
            return new RunRecord(cell.Case, cell.Arm, cell.Repeat, lane) { HarnessError = refusal };
        }

        var env = EnvironmentFor(cell);
        var dataDir = DataDirFor(cell, lane);
        // A stale session is the previous campaign, not this one: it remembers the configuration it
        // was opened with and how far its stages got. One cost a whole run — every round came out on
        // the DEFAULT rounds and thresholds while the operator had set their own.
        // The run's own ref, created (or moved back) at the reviewed commit. Left in place afterwards:
        // a `bench/*` ref is cheap, the server's worktree may still have it checked out, and the
        // session file on disk names it.
        if (cell.Case.Commit.Length > 0)
        {
            var (exit, said) = await Git.RunAsync(options.Repo, ["branch", "-f", BranchFor(cell), RefTarget(cell.Case)], ct);
            if (exit != 0)
            {
                return new RunRecord(cell.Case, cell.Arm, cell.Repeat, lane) { HarnessError = $"git branch -f {BranchFor(cell)}: {said}" };
            }
        }

        Sessions.Reset(dataDir, options.Repo, BranchFor(cell));
        await using var client = new GateClient(options.Executable, dataDir, env);
        var run = await new RoundRunner(client, options.Repo, options.Timeout)
            .RunAsync(cell.Case, cell.Arm, cell.Repeat, lane, options.Stages, BranchFor(cell));

        // The disk, not the answer. They came apart once and nothing said so — and neither did
        // the settings, which is why what was ASKED for is compared with what the session got.
        var onDisk = OnDisk.Read(dataDir, options.Repo, BranchFor(cell));

        return run with
        {
            OnDisk = onDisk,
            Settings = SettingsCheck.Of(env, onDisk.Config, run.Stages),
        };
    }

    /// <summary>
    /// A data directory per RUN by default, and one SHARED when the windows case is being measured.
    /// </summary>
    /// <remarks>
    /// The distinction is the measurement. Comparing two models wants them not to interfere; asking
    /// what five windows do to each other wants exactly the interference, and that lives in the
    /// shared directory — the sessions, the engine lease, the caller memory.
    /// </remarks>
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

        // The disk is printed beside the answer, because "12 findings" and "the record was never
        // written" have looked identical from the answer alone. And the resolve's own reply, because
        // that is where "can these findings be acted on" is actually answered.
        var disk = run.OnDisk is null || run.Stages.Count == 0
            ? string.Empty
            : run.OnDisk.Clean
                ? "  [clean]"
                : $"  [DIRTY: {run.OnDisk.StillRunning} still running, "
                    + $"{run.OnDisk.Pending} pending{Note(run.OnDisk.Note)}]";
        var refused = run.Stages.Where(s => s.ResolveRefused.Length > 0).ToList();
        var resolve = refused.Count == 0
            ? string.Empty
            : $"  [RESOLVE REFUSED: {string.Join("; ", refused.Select(s => $"{s.Stage}: {s.ResolveRefused}"))}]";

        // A setting that was accepted and did nothing looks exactly like one that worked, so the
        // disagreement is printed the moment it happens rather than found in a table afterwards.
        var applied = run.Settings is null || run.Settings.Ok
            ? string.Empty
            : $"  [SETTINGS NOT APPLIED: {string.Join("; ", run.Settings.Mismatches)}]";

        return $"{run.Arm,-22} {run.Case.Name,-34} #{run.Repeat} {stages}{disk}{resolve}{applied}";
    }

    private static string Note(string note) => note.Length == 0 ? string.Empty : $", {note}";
}
