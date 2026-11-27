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

    /// <summary>
    /// V35 (epoch 3 E3 s2 t5 d8): the names, with the real codex and NO model call. The live list is read in the real unit of the job (the working folder and the wall time are recorded), the default 'terra' and the family 'astra' are resolved by the product's own choice (<see cref="CodexModelList.Newest"/>), the full name
    /// 'gpt-5.6-terra' is used as given, and for each the preflight is asked with that model and its rows are recorded; the tool report of each (<c>codex debug prompt-input</c> with the flags of the launch) is read with the grammar of <see cref="CodexToolList.Read"/>, and — when <c>COAI_REAL_CODEX_FIXTURES</c> names a folder —
    /// the 'additional_tools' item of the report of the two models of the fixtures is saved there. The sha256 of the real auth.json is compared before and after and only 'equal' or 'changed' is written, never a token. Phase 'names'.
    /// </summary>
    [Fact]
    public async Task Names_OfTheRealCodex_AreResolvedFromTheLiveList_TheToolReportsAreRecorded_AndNoModelIsCalled()
    {
        Need("names");
        using var home = new TempHome();
        var report = new Report();
        var authFile = Path.Combine(Env("COAI_REAL_CODEX_HOME"), ".codex", "auth.json");
        string Hash() => File.Exists(authFile) ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(authFile))) : "absent";
        var before = Hash();
        try
        {
            await using var coder = await CoderBed.CreateAsync();
            var host = RealHost(coder.Host);
            var vendor = CodexCoderVendor.Instance;
            var exe = Env("COAI_REAL_CODEX_EXE");
            var job = coder.Job;
            Directory.CreateDirectory(CodexCoderVendor.CodexHome(job));
            var boundary = coder.Boundary(host: host).For(vendor);
            var turns = coder.Turns(boundary);
            var signIns = new SignInJobs();
            var budget = TimeSpan.FromMinutes(10) + UnitControl.Backstop;
            (await boundary.PrepareSignInAsync(job, exe, budget, signIns, DateTime.UtcNow, TestContext.Current.CancellationToken)).Should().BeEmpty();
            report.Line("the product makes the job sign-in copy: ok");

            var folder = CodexCoderVendor.ModelListFolder(job);
            string[] FilesOfHome() => Directory.Exists(CodexCoderVendor.CodexHome(job)) ? [.. Directory.EnumerateFileSystemEntries(CodexCoderVendor.CodexHome(job), "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(CodexCoderVendor.CodexHome(job), path)).Order(StringComparer.Ordinal)] : [];
            var filesBefore = FilesOfHome();
            // The names of the processes of the unit, sampled while the read runs (A4-M3 item 4): the control group of the unit, read every 20 ms.
            var seen = new SortedSet<string>(StringComparer.Ordinal);
            using var stopSampling = new CancellationTokenSource();
            var sampler = Task.Run(async () =>
            {
                var unitName = CoderUnit.UnitName(job, "m1") + ".service";
                while (!stopSampling.IsCancellationRequested)
                {
                    try
                    {
                        var group = (await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "show", "-p", "ControlGroup", "--value", unitName], coder.Worktree) { Timeout = TimeSpan.FromSeconds(5) })).StdOut.Trim();
                        var procs = Path.Combine("/sys/fs/cgroup", group.TrimStart('/'), "cgroup.procs");
                        if (group.Length > 0 && File.Exists(procs))
                        {
                            foreach (var pid in File.ReadAllLines(procs))
                            {
                                if (File.Exists($"/proc/{pid}/comm"))
                                {
                                    seen.Add(File.ReadAllText($"/proc/{pid}/comm").Trim());
                                }
                            }
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // the process ended between the two looks
                    }
                }
            });
            var clock = Stopwatch.StartNew();
            var listed = await turns.RunModelFreeAsync(job, new ProcessRequest(exe, CodexModelList.Arguments, folder) { Timeout = CodexModelList.Deadline }, "m1", vendor, signIns, TestContext.Current.CancellationToken);
            await stopSampling.CancelAsync();
            await sampler;
            var filesAfter = FilesOfHome();
            report.Line($"files of the job's CODEX_HOME before the read: {string.Join(", ", filesBefore)}");
            report.Line($"files of the job's CODEX_HOME after the read, not there before: {(filesAfter.Except(filesBefore).Any() ? string.Join(", ", filesAfter.Except(filesBefore)) : "none")}");
            report.Line($"names of the processes seen in the unit of the read (sampled; the unit is gone before the last sample may see more): {string.Join(", ", seen)}");
            report.Line($"list read (codex debug models, live, in the real unit): {clock.Elapsed.TotalSeconds:F1} s, outcome {listed.GetType().Name}");
            report.Line($"working folder of the list read: <job>/coder/codex/models-cwd (inside the job's vendor folder: {folder.StartsWith(job.CoderVendorFolder("codex"), StringComparison.Ordinal)}); entries in it after the run: {(Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).Count() : -1)}");
            var finished = listed.Should().BeOfType<CoderTurnOutcome.Finished>(listed.ToString()).Subject;
            var list = CodexModelList.Read(finished.Run.Result.StdOut);
            list.Should().NotBeNull("the live list could not be read with the job's own sign-in copy: the task stops here");
            report.Line($"the list holds {list!.Count} models; visible (list): {string.Join(", ", list.Where(model => model.Visibility == "list").Select(model => model.Slug))}");

            if (Env("COAI_REAL_CODEX_LIST_FIXTURE").Length > 0)
            {
                // The recording the fake plays (role-stops format), scrubbed to the four fields coai reads: slug, display_name, visibility, priority.
                using var live = JsonDocument.Parse(finished.Run.Result.StdOut);
                var kept = live.RootElement.GetProperty("models").EnumerateArray().Select(model => new Dictionary<string, object?> { ["slug"] = model.GetProperty("slug").GetString(), ["display_name"] = model.TryGetProperty("display_name", out var shown) ? shown.GetString() : null, ["visibility"] = model.TryGetProperty("visibility", out var visibility) ? visibility.GetString() : null, ["priority"] = model.TryGetProperty("priority", out var priority) && priority.TryGetInt32(out var number) ? number : null }).ToList();
                var recording = new Dictionary<string, object?> { ["cli"] = "codex", ["cliVersion"] = "codex-cli 0.171.0", ["recordedUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), ["recordedBy"] = "epoch 3 E3 s2 t5 d8 (codex debug models, live, no model call; scrubbed to slug, display_name, visibility and priority)", ["how"] = "codex-debug-models", ["argv"] = new[] { "debug", "models" }, ["exitCode"] = 0, ["stdout"] = JsonSerializer.Serialize(new { models = kept }), ["stderr"] = string.Empty };
                File.WriteAllText(Env("COAI_REAL_CODEX_LIST_FIXTURE"), JsonSerializer.Serialize(recording, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                report.Line($"list recording written ({kept.Count} models, scrubbed)");
            }

            var families = CoaiMcp.Core.Roles.RoleModelNames.Current.CodexFamilies;
            var resolved = new[] { ("terra", CodexModelList.Newest(list, "terra", families)), ("gpt-5.6-terra", "gpt-5.6-terra"), ("astra", CodexModelList.Newest(list, "astra", families)) };
            var part = 1;
            foreach (var (asked, model) in resolved)
            {
                report.Line();
                report.Line($"### asked '{asked}', resolved '{model}'");
                model.Should().NotBeNull();
                var request = new ProcessRequest(exe, vendor.FreshArgv(model!, coder.Worktree, new CoderLaunchFiles(Path.Combine(job.JobRoot, "report.txt"))), coder.Worktree);
                vendor.PrepareLaunch(host, job, request).Problem.Should().BeEmpty();
                var wall = Stopwatch.StartNew();
                var admission = await boundary.AdmitAsync(job, exe, TestContext.Current.CancellationToken, false, budget);
                report.Line($"preflight wall time {wall.Elapsed.TotalSeconds:F1} s, admission {admission.GetType().Name}");
                var rows = admission is BoundaryAdmission.Admitted admitted ? admitted.Records.Values.SelectMany(record => record.Preflight).ToList() : [.. ((BoundaryAdmission.Refused)admission).Record.Preflight];
                report.Line($"rows: {rows.Count}, held {rows.Count(row => row.Held)}; red: {string.Join(", ", rows.Where(row => !row.Held).Select(row => row.Item))}");
                report.Line($"tool-list: {rows.Single(row => row.Item == "tool-list").Detail}");
                report.Line($"no-project-tools: {rows.Single(row => row.Item == "no-project-tools").Detail}");

                var printed = await turns.RunModelFreeAsync(job, new ProcessRequest(exe, ["debug", "prompt-input", .. CodexCoderVendor.ProbeFlags(model!), "x"], folder) { Timeout = TimeSpan.FromSeconds(60) }, $"m{++part}", vendor, signIns, TestContext.Current.CancellationToken);
                var text = printed.Should().BeOfType<CoderTurnOutcome.Finished>(printed.ToString()).Subject.Run.Result.StdOut;
                var read = CodexToolList.Read(text);
                report.Line($"the tool report read with the grammar: whole {read.Whole}, {read.Names.Count} names, outside the known set: {CodexToolList.Outside(read.Names).Count}, absent known tools: {string.Join(", ", CodexToolList.Absent(read.Names))}");
                if (Env("COAI_REAL_CODEX_FIXTURES").Length > 0 && model is "gpt-6.1-terra" or "gpt-5.6-terra")
                {
                    using var document = JsonDocument.Parse(text);
                    var tools = document.RootElement.EnumerateArray().First(item => item.TryGetProperty("type", out var type) && type.GetString() == "additional_tools");
                    // The item is copied as the CLI printed it: its raw text, not a serialisation of it, so the scrub changes no byte of the additional_tools item (A4-M1).
                    var raw = tools.GetRawText();
                    var head = JsonSerializer.Serialize(new { cli = "codex", cliVersion = "codex-cli 0.171.0", recordedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), recordedBy = "epoch 3 E3 s2 t5 d8 (codex debug prompt-input with the flags of the launch; no model call; only the additional_tools item is kept, byte for byte)", model });
                    var fixture = head[..^1] + ",\"report\":[" + raw + "]}";
                    Directory.CreateDirectory(Env("COAI_REAL_CODEX_FIXTURES"));
                    var path = Path.Combine(Env("COAI_REAL_CODEX_FIXTURES"), model + ".json");
                    File.WriteAllText(path, fixture + "\n");
                    using var saved = JsonDocument.Parse(File.ReadAllText(path));
                    report.Line($"fixture written: {model}.json ({fixture.Length} characters); the saved additional_tools item is byte for byte the item the CLI printed: {saved.RootElement.GetProperty("report")[0].GetRawText() == raw}");
                }
            }
        }
        finally
        {
            report.Line();
            report.Line($"the real auth.json of codex: {(Hash() == before ? "equal" : "changed")} before and after (sha256 compared, nothing printed)");
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
