using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The REAL codex through the product's own coder road (epoch 3 E3, pass 7 follow-up): the preflight in the real unit with no model call, and two paid calls (turn 0 and one fix turn) on a made-up task in a scratch repository. The flip is NOT made: the table of the
/// scenario is a test table (the one <see cref="CodexRigWorld.Vendors"/> makes), the product's own table keeps the mark off.
/// </summary>
/// <remarks>
/// <para>Runs ONLY when <c>COAI_REAL_CODEX_PHASE</c> is <c>preflight</c> or <c>calls</c> — any other value is a skip, never a pass. The process HOME is a temp folder for the whole test; the person's real home is named by <c>COAI_REAL_CODEX_HOME</c> and is read
/// only by the product's own sign-in copy (the recipe of the codex vendor). The program is <c>COAI_REAL_CODEX_EXE</c>. What the test writes (redacted argv, times, exit codes, rows, the answers of the turn check) goes to the file <c>COAI_REAL_CODEX_RESULTS</c>:
/// never a token, never an environment, never the content of a sign-in file.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheRealCodexCoderRunTests
{
    private static string Model => Environment.GetEnvironmentVariable("COAI_REAL_CODEX_MODEL") is { Length: > 0 } named ? named : "gpt-6.1-terra";

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;

    private static void Need(string phase)
    {
        Assert.SkipUnless(Env("COAI_REAL_CODEX_PHASE") == phase, $"the real codex runs only with COAI_REAL_CODEX_PHASE={phase} — this is a skip, not a pass");
        Assert.SkipUnless(Env("COAI_REAL_CODEX_HOME").Length > 0 && Env("COAI_REAL_CODEX_EXE").Length > 0 && Env("COAI_REAL_CODEX_RESULTS").Length > 0, "COAI_REAL_CODEX_HOME, _EXE and _RESULTS must be set");
    }

    private sealed class Report
    {
        private readonly StringBuilder _text = new();

        public void Line(string text = "") => _text.AppendLine(text);

        public void Flush() => File.AppendAllText(Env("COAI_REAL_CODEX_RESULTS"), _text.ToString());
    }

    /// <summary>The process HOME is a temp folder while the scenario runs.</summary>
    private sealed class TempHome : IDisposable
    {
        private readonly string? _before = Environment.GetEnvironmentVariable("HOME");
        private readonly TempDir _dir = TempDir.For("coai-real-codex-home-");

        public TempHome() => Environment.SetEnvironmentVariable("HOME", _dir.Path);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HOME", _before);
            _dir.Dispose();
        }
    }

    private static BoundaryHost RealHost(BoundaryHost host) => host with { RealHome = Env("COAI_REAL_CODEX_HOME"), NuGetPackages = string.Empty, Path = string.Empty };

    [Fact]
    public async Task Preflight_OfTheRealCodex_InTheRealUnit_MakesNoModelCall()
    {
        Need("preflight");
        using var home = new TempHome();
        var report = new Report();
        try
        {
            await using var coder = await CoderBed.CreateAsync();
            var host = RealHost(coder.Host);
            var vendor = CodexCoderVendor.Instance;
            var exe = Env("COAI_REAL_CODEX_EXE");
            var job = coder.Job;
            Directory.CreateDirectory(CodexCoderVendor.CodexHome(job));
            var boundary = coder.Boundary(host: host).For(vendor);
            var signIn = await boundary.PrepareSignInAsync(job, exe, TimeSpan.FromMinutes(10) + UnitControl.Backstop, new SignInJobs(), DateTime.UtcNow, TestContext.Current.CancellationToken);
            report.Line($"the product makes the job sign-in copy: problem '{signIn}'");
            signIn.Should().BeEmpty();
            var request = new ProcessRequest(exe, vendor.FreshArgv(Model, coder.Worktree, new CoderLaunchFiles(Path.Combine(job.JobRoot, "report.txt"))), coder.Worktree);
            var prepared = vendor.PrepareLaunch(host, job, request);
            report.Line($"prepared launch problem: '{prepared.Problem}'");
            prepared.Problem.Should().BeEmpty();
            report.Line("argv of the launch after the vendor prepared it (the worktree path is replaced by <worktree>):");
            report.Line("`" + string.Join(' ', prepared.Request.Arguments.Select(argument => argument.Replace(coder.Worktree, "<worktree>", StringComparison.Ordinal))) + "`");

            var clock = Stopwatch.StartNew();
            var admission = await boundary.AdmitAsync(job, exe, TestContext.Current.CancellationToken, false, TimeSpan.FromMinutes(10) + UnitControl.Backstop);
            report.Line($"preflight wall time: {clock.Elapsed.TotalSeconds:F1} s");
            report.Line($"admission: {admission.GetType().Name}");
            var rows = admission switch
            {
                BoundaryAdmission.Admitted admitted => admitted.Records.Values.SelectMany(record => record.Preflight).ToList(),
                BoundaryAdmission.Refused refused => [.. refused.Record.Preflight],
                _ => [],
            };
            report.Line();
            report.Line("| row | held | detail |");
            report.Line("|---|---|---|");
            foreach (var row in rows)
            {
                report.Line($"| {row.Item} | {row.Held} | {row.Detail.Replace('|', '/').Replace('\n', ' ')} |");
            }

            foreach (var folder in new[] { coder.Main, coder.CoderRoot })
            {
                var left = Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Where(entry => Path.GetFileName(entry).StartsWith(CodexPreflightScript.CoverMarker, StringComparison.Ordinal)).ToList() : [];
                report.Line($"cover markers left in {(folder == coder.Main ? "the main checkout" : "the coder root")}: {left.Count}");
                left.Should().BeEmpty();
            }

            if (admission is BoundaryAdmission.Admitted)
            {
                report.Line($"verdict: every row held ({rows.Count} rows)");
            }

            admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString());
        }
        finally
        {
            report.Flush();
        }
    }

    /// <summary>The recorded answers of the turn check, kept beside the real vendor, which does every other thing itself.</summary>
    private sealed class WatchedCodex(Report report) : ICoderVendor
    {
        private readonly CodexCoderVendor _real = CodexCoderVendor.Instance;

        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private int _checks;

        private int _launches;

        public string Id => _real.Id;

        public string PromptId => _real.PromptId;

        public string PromptResource => _real.PromptResource;

        public CoderSignInRecipe SignIn => _real.SignIn;

        public IConsultantRuntime Runtime => _real.Runtime;

        public string FixClause => _real.FixClause;

        public bool PointsAtInstructionBlock => _real.PointsAtInstructionBlock;

        public TimeSpan? TurnDeadline => ((ICoderVendor)_real).TurnDeadline;

        public string NotProvedByPreflight => ((ICoderVendor)_real).NotProvedByPreflight;

        public string BeforeStart(BoundaryJob job) => ((ICoderVendor)_real).BeforeStart(job);

        public CoderPreflightPlan? PreflightPlan(BoundaryHost host, BoundaryJob job, CoderProgram program, string windowsCopy, TimeSpan runtimeMax) => ((ICoderVendor)_real).PreflightPlan(host, job, program, windowsCopy, runtimeMax);

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => _real.ProbeAsync(launcher, executable, workingDirectory, ct);

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => _real.FreshArgv(model, worktree);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => _real.ResumedArgv(model, worktree, sessionId);

        public IReadOnlyList<string> FreshArgv(string model, string worktree, CoderLaunchFiles files) => _real.FreshArgv(model, worktree, files);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId, CoderLaunchFiles files) => _real.ResumedArgv(model, worktree, sessionId, files);

        public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) => _real.UnitProperties(host, job, program, runtimeMax);

        public string WorkingDirectory(BoundaryJob job) => _real.WorkingDirectory(job);

        public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => _real.Environment(host, job);

        public CoderHomeLayout Home(BoundaryJob job) => _real.Home(job);

        public CoderLaunchPrepared PrepareLaunch(BoundaryHost host, BoundaryJob job, ProcessRequest request)
        {
            // The cap of the order: two paid calls at most. A third launch is refused HERE, before a process is started.
            if (++_launches > 2)
            {
                report.Line($"[{_clock.Elapsed.TotalSeconds:F0} s] launch {_launches} REFUSED by the harness: the cap is two paid calls");

                return new CoderLaunchPrepared(request, "the harness allows two paid calls and this would be the third");
            }

            var prepared = ((ICoderVendor)_real).PrepareLaunch(host, job, request);
            report.Line($"[{_clock.Elapsed.TotalSeconds:F0} s] launch prepared; problem '{prepared.Problem}'; argv (the worktree path is replaced by <worktree>):");
            report.Line("`" + string.Join(' ', prepared.Request.Arguments.Select(argument => argument.Replace(job.Tree.Root, "<worktree>", StringComparison.Ordinal))) + "`");

            return prepared;
        }

        public void CollectLaunch(BoundaryJob job, ProcessRequest original, ProcessRequest launched) => ((ICoderVendor)_real).CollectLaunch(job, original, launched);

        public CoderProgram Program(string executable, string path, BoundaryJob job) => _real.Program(executable, path, job);

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession) => Watched(process, _real.CheckTurn(process, expectedSession));

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession, BoundaryJob job) => Watched(process, _real.CheckTurn(process, expectedSession, job));

        private CoderTurnVerdict Watched(ProcessResult process, CoderTurnVerdict verdict)
        {
            var kinds = new SortedDictionary<string, int>();
            var items = new SortedDictionary<string, int>();
            foreach (var line in process.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var kind = root.TryGetProperty("type", out var type) ? type.GetString() ?? "?" : "?";
                    kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                    if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var itemType))
                    {
                        var name = itemType.GetString() ?? "?";
                        items[name] = items.GetValueOrDefault(name) + 1;
                    }
                }
                catch (JsonException)
                {
                    kinds["<line that is not json>"] = kinds.GetValueOrDefault("<line that is not json>") + 1;
                }
            }

            var number = ++_checks;
            report.Line($"[{_clock.Elapsed.TotalSeconds:F0} s] turn check {number}: exit {process.ExitCode}, timedOut {process.TimedOut}, cancelled {process.Cancelled}, stdout {process.StdOut.Length} bytes");
            report.Line($"  event types: {string.Join(", ", kinds.Select(pair => $"{pair.Key} x{pair.Value}"))}");
            report.Line($"  item types: {string.Join(", ", items.Select(pair => $"{pair.Key} x{pair.Value}"))}");
            report.Line($"  a reasoning item appears: {items.ContainsKey("reasoning")}");
            report.Line($"  the turn check answers: {verdict.GetType().Name}{(verdict is CoderTurnVerdict.Fine ? string.Empty : " — " + verdict)}");

            return verdict;
        }
    }

    private static string TaskYaml() => WorkPlanYaml.Task(new BaTask(
        CoderRig.TaskId,
        "Create the file src/thing.txt holding exactly one line: state: ready",
        "code",
        4,
        [],
        false,
        ["src/thing.txt"],
        [new BaDod("d1", "src/thing.txt exists and holds the line: state: ready"), new BaDod("d2", "no other file of the repository is changed")],
        null));

    /// <summary>The checks: the test step wants a word the task never names, so the first turn ends red and the digest of the failure is what the fix turn works from.</summary>
    private static string Toolchain() => ToolchainFixture.Shell(
        "echo built",
        "if [ -f src/thing.txt ] && grep -q 'state: ready' src/thing.txt && grep -qi kestrel src/thing.txt; then echo tested; else echo 'FAILED thing: src/thing.txt must hold the line state: ready and also the word kestrel'; exit 1; fi");

    [Fact]
    public async Task TwoPaidCalls_TurnZeroAndOneFixTurn_ThroughTheProductsRoad()
    {
        Need("calls");
        using var home = new TempHome();
        var report = new Report();
        try
        {
            await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: new Dictionary<string, string> { [CoderRig.TaskFile] = TaskYaml() });
            rig.Vendors = CoderVendorSet.Of(CoderBoundaryTable.Parse("""
                { "version": 1, "judgement": "j.md", "shared": [],
                  "vendors": [ { "id": "codex", "verdict": "ship", "results": "r.md",
                    "launchable": true, "measured": [ { "release": "codex-cli 0.171.0", "resultsHeading": "H", "judgementHeading": "J", "programs": [ { "file": "x", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ],
                    "properties": [], "pending": [] } ] }
                """), new WatchedCodex(report));
            rig.OnHost = host => RealHost(host);
            rig.Rig.Setting("COAI_ROLE_CODEX_CLI", Env("COAI_REAL_CODEX_EXE"));
            rig.Rig.Setting("COAI_FIX_ROUNDS", "1");
            rig.AttemptTimeout = TimeSpan.FromMinutes(12);
            var job = rig.NewJob(route: [$"codex/{Model}"], timeoutMinutes: 30);
            report.Line($"job {job.JobId}; route codex/{Model}; fix rounds 1; effort {CodexCoderVendor.Effort}");

            var clock = Stopwatch.StartNew();
            var (exit, ended) = await rig.RunAsync(job);
            report.Line($"the job ended after {clock.Elapsed.TotalSeconds:F0} s: exit {exit}, state {ended.State}, stop '{ended.Stop}', reason '{ended.Reason}'");
            try
            {
                var envelope = rig.Envelope(ended).GetRawText();
                report.Line("envelope (the product's own result of the job):");
                report.Line("```json");
                report.Line(envelope.Length > 6000 ? envelope[..6000] + " …" : envelope);
                report.Line("```");
            }
            catch (Exception problem) when (problem is InvalidOperationException or IOException or JsonException or NullReferenceException)
            {
                report.Line($"no envelope could be read ({problem.GetType().Name})");
            }
        }
        finally
        {
            report.Flush();
        }
    }
}
