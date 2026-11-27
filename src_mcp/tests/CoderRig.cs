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
/// One coder scenario's world (epoch 3 E2 s3 t3): a real git repository holding a small work plan with ONE task (two dod ids), a coder root of its own, the real <see cref="RoleRunner"/> running the real
/// <see cref="CoderWork"/>, and FakeCli standing in for claude — it edits files below its working folder (<c>writeFiles</c> of the model map) and answers with a coder report. No model is called.
/// </summary>
/// <remarks>
/// <para>The coder's child starts on the MINIMAL environment, which carries no <c>FAKECLI_*</c> name, so the fake is steered for it by the steering file beside its binary (<see cref="FakeCliSteering"/>); the capability probe
/// (<c>--version</c>, <c>--help</c>) is started with the runner's own environment and is steered by the variables. Both say what claude 2.1.330 says. In the serial <c>fakecli-env</c> collection, like every class that steers the fake.</para>
/// <para>The repository has a LOCAL git identity: coai's own commit in the worktree is made through the hardened door, which cannot carry a <c>-c user.name</c>, so the identity must come from the configuration.</para>
/// <para><b>The coder runs inside the OS layer</b> (epoch 3 E2, s2/s3 integration), in a real <c>systemd-run --user</c> unit — so the rig needs a user manager (<see cref="CreateAsync"/> skips the test without one, unless the scenario never launches a
/// coder) and a stand-in for the person's home (the one sign-in file the layer binds is a made-up line in a temp folder; the real home is never read). The unit replaces <c>/tmp</c>, so the fake's model map, its launch counter and its record folder live in the
/// JOB's own temp folder (<see cref="JobTemp"/>), which the unit sees as <c>/tmp</c>: <see cref="Stage"/> puts the map there and points the steering file at the in-unit paths just before a job runs. Every unit a job started is stopped at <see cref="DisposeAsync"/>, and a unit left is a failed test.</para>
/// </remarks>
[Collection("fakecli-env")]
internal sealed class CoderRig : IAsyncDisposable
{
    internal const string Story = "plan/epoch-1/e1-core/stories/s1-first";

    internal const string TaskId = "t1-a";

    internal const string TaskFile = $"{Story}/tasks/{TaskId}/task.yaml";

    internal static readonly string[] Dod = ["d1", "d2"];

    private readonly RoleFailoverRig _inner;
    private readonly TempDir _root = TempDir.For("coai-coder-root-");
    private readonly TempDir _jobs = TempDir.For("coai-coder-jobs-");
    private readonly TempDir _home = TempDir.For("coai-coder-home-");
    private readonly List<string> _staged = [];
    private IDisposable _steering = FakeCliSteering.Unwritten;

    private CoderRig(RoleFailoverRig inner)
    {
        _inner = inner;
        _inner.Setting("COAI_CODER_ROOT", _root.Path);
        _inner.Setting(CoderOsLayer.JobsVariable, _jobs.Path);
        Directory.CreateDirectory(Path.Combine(_home.Path, ".claude"));
        File.WriteAllText(Path.Combine(_home.Path, ".claude", ".credentials.json"), CoderBed.SignInLine + "\n");
    }

    internal RoleFailoverRig Rig => _inner;

    internal string Repo => _inner.Repo;

    internal string Root => _root.Path;

    internal RoleJobWorld World => _inner.World;

    /// <summary>The commit the repository is on when the scenario starts — what a coder's base is when the request names none.</summary>
    internal string Base { get; private set; } = string.Empty;

    /// <summary>The machine as the OS layer sees it: this host, the stand-in as the person's home, no PATH to look programs up in.</summary>
    /// <summary>Writes a file into the stand-in for the person's real home (a sign-in file of a vendor that signs in with one).</summary>
    internal void RealHomeFile(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_home.Path, relative))!);
        File.WriteAllText(Path.Combine(_home.Path, relative), text);
    }

    internal BoundaryHost Host => OnHost(BoundaryHost.Current() with { RealHome = _home.Path, NuGetPackages = string.Empty, Path = string.Empty });

    /// <summary>The vendors a coder of the job may be launched on: the product's set, or a test's own table with its own test vendor (epoch 3 E3 s7 t2). Read when the job's work is made.</summary>
    internal CoderVendorSet Vendors { get; set; } = CoderVendors.Shipped;

    /// <summary>A test-only change of the machine (no user manager, another operating system) — read when the job's work is made.</summary>
    internal Func<BoundaryHost, BoundaryHost> OnHost { get; set; } = host => host;

    /// <summary>A test-only rewrite of every unit's argument list (a property taken away); null leaves the layer as it ships.</summary>
    internal Func<IReadOnlyList<string>, IReadOnlyList<string>>? Degrade { get; set; }

    /// <summary>How long one coder launch may run before coai stops its unit; thirty seconds unless a test needs a shorter wait.</summary>
    internal TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The line a fixture file of <see cref="StateFile"/> says to make the test step fail, and the one that lets it pass.</summary>
    internal const string Red = "state: red";

    internal const string Green = "state: green";

    /// <summary>What the fixture build looks for: a file below <c>src</c> that holds it does not build.</summary>
    internal const string Broken = "BUILD_BREAK";

    /// <summary>The file the fake coder writes to flip the fixture's checks.</summary>
    internal const string StateFile = "src/thing.cs";

    /// <summary>
    /// The toolchain file of a fixture repository (<c>.coai/toolchain.yaml</c>, read at the base commit by the real reader): a REAL tiny build and test, no shell script of the repository and nothing mocked but the vendor. The build fails — printing an
    /// MSBuild-shaped error line, as a compiler would — when a file below <c>src</c> holds <see cref="Broken"/>; the test fails — printing what failed — when one holds <see cref="Red"/>. Both write nothing, so a green run leaves the worktree as it found it.
    /// </summary>
    internal static string DefaultToolchain(string buildTail = "", string testTail = "") =>
        ToolchainFixture.Shell(
            $"if grep -rq {Broken} src 2>/dev/null; then echo 'src/thing.cs(1,1): error CS1002: ; expected [app.csproj]'; exit 1; fi; echo built{buildTail}",
            $"if grep -rq '{Red}' src 2>/dev/null; then echo 'FAILED thing: the state is red'; exit 1; fi; echo tested{testTail}");

    /// <summary>
    /// The rig. A coder launch needs a systemd user manager, so without one the test is SKIPPED, with the reason — unless <paramref name="needsLayer"/> is false, for a scenario in which no coder is ever launched.
    /// </summary>
    /// <param name="toolchainOf">The same, made from the path of the rig's stand-in for the person's home — for a scenario whose files must name it.</param>
    /// <param name="toolchain">The text of <c>.coai/toolchain.yaml</c> the BASE commit holds: null is <see cref="DefaultToolchain"/> (a build and a test that pass until a file says otherwise), empty is a repository with no toolchain file.</param>
    internal static async Task<CoderRig> CreateAsync(bool needsLayer = true, string? toolchain = null, IReadOnlyDictionary<string, string>? extra = null, Func<string, string>? toolchainOf = null)
    {
        if (needsLayer)
        {
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
        }

        var rig = new CoderRig(await RoleFailoverRig.CreateAsync());
        rig.Base = await LayFixtureAsync(rig.Rig.Git, toolchainOf is null ? toolchain : toolchainOf(rig._home.Path), extra);
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "2.1.330 (Claude Code)\n");
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "help-2.1.330-wsl.txt")));

        return rig;
    }

    /// <summary>
    /// Lays the fixture of a coder scenario into <paramref name="repo"/> and commits it: the plan with ONE task (<see cref="TaskFile"/>), the toolchain file (<paramref name="toolchain"/>, as <see cref="CreateAsync"/> reads it) and any <paramref name="extra"/>
    /// files, with a LOCAL git identity (coai's own commit goes through the hardened door, which cannot carry one). Answers the commit the repository is on — the base of the job. One copy for the in-process rig and the real-binary scenarios.
    /// </summary>
    internal static async Task<string> LayFixtureAsync(TempGitRepo repo, string? toolchain = null, IReadOnlyDictionary<string, string>? extra = null)
    {
        BaScenario.Lay(repo.Path);
        BaScenario.Write(repo.Path, $"{Story}/story.yaml", WorkPlanYaml.Story(new BaStory("s1-first", "A story", [], [])));
        BaScenario.Write(repo.Path, TaskFile, WorkPlanYaml.Task(new BaTask(TaskId, "Write the thing", "code", 4, [], false, ["src/thing.cs"], [new BaDod("d1", "the thing works"), new BaDod("d2", "the thing is tested")], null)));
        if ((toolchain ?? DefaultToolchain()) is { Length: > 0 } toolchainText)
        {
            BaScenario.Write(repo.Path, ".coai/toolchain.yaml", toolchainText);
        }

        foreach (var (name, text) in extra ?? new Dictionary<string, string>())
        {
            BaScenario.Write(repo.Path, name, text);
        }

        foreach (var (key, value) in new[] { ("user.email", "t@t"), ("user.name", "t"), ("commit.gpgsign", "false") })
        {
            await repo.GitAsync("config", key, value);
        }

        await repo.CommitAsync("the plan and its task");

        return await repo.HeadAsync();
    }

    /// <summary>
    /// One launch of the fake coder: the files it writes below its working folder, what it says, how long it waits before it answers, and — a coder that goes where it must not — the ABSOLUTE paths it tries to write (what the OS refuses
    /// goes to its stderr).
    /// </summary>
    internal sealed record Launch(IReadOnlyDictionary<string, string>? Edits, string Answer, int SleepMs = 0, IReadOnlyList<string>? TryWrites = null, string Session = "", int Exit = 0, string Stderr = "", string Stdout = "", IReadOnlyDictionary<string, string>? HomeFiles = null, IReadOnlyList<string>? Reads = null, bool IgnoreTerm = false, string OutfileText = "");

    /// <summary>
    /// What FakeCli does for <paramref name="model"/>, launch by launch (the last repeats). The steering file — the coder's child reads only that — is written again with every call.
    /// </summary>
    internal CoderRig Says(string model, params Launch[] launches)
    {
        _inner.Says(model, [.. launches.Select(launch => (object)Entry(launch))]);
        Steer(_inner.MapFile, _inner.RecordFolder);

        return this;
    }

    /// <summary>What FakeCli does for <paramref name="model"/> when each launch is a ready model-map entry (an agy coder: <c>agyTools</c> and the rest, <see cref="AgyCoderBed"/>) — the steering file is written again, as for <see cref="Says(string, Launch[])"/>.</summary>
    internal CoderRig SaysEntries(string model, params IReadOnlyDictionary<string, object?>[] entries)
    {
        _inner.Says(model, [.. entries.Select(entry => (object)entry)]);
        Steer(_inner.MapFile, _inner.RecordFolder);

        return this;
    }

    private readonly Dictionary<string, string> _extra = [];

    /// <summary>More steering names for the fake besides the map and the record folder (epoch 3 E3 s2 t5: the list <c>codex debug models</c> answers, the tool-list mode). Written with the next staging, and in the steering file at once.</summary>
    internal CoderRig Steering(params (string Name, string Value)[] names)
    {
        foreach (var (name, value) in names)
        {
            _extra[name] = value;
        }

        Steer(_lastMap, _lastRecord);

        return this;
    }

    private string _lastMap = string.Empty;

    private string _lastRecord = string.Empty;

    private void Steer(string map, string record)
    {
        _lastMap = map;
        _lastRecord = record;
        _steering.Dispose();
        _steering = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_MODEL_MAP"] = map, ["FAKECLI_RECORD_DIR"] = record }.Concat(_extra).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>One model-map entry: claude's recorded success envelope around the answer, and — only when there are edits — the <c>writeFiles</c> object (a null would be read by the fake as an edit it cannot make).</summary>
    internal static Dictionary<string, object?> Entry(Launch launch)
    {
        var entry = new Dictionary<string, object?>
        {
            ["stdout"] = launch.Stdout.Length > 0 ? launch.Stdout : launch.Exit != 0 ? string.Empty : WithSession(JsonSerializer.SerializeToElement(FakeRoleAnswers.ClaudeSays(launch.Answer)).GetProperty("stdout").GetString()!, launch.Session),
            ["exit"] = launch.Exit,
            ["sleepMs"] = launch.SleepMs,
        };
        if (launch.Edits is not null)
        {
            entry["writeFiles"] = launch.Edits;
        }

        if (launch.TryWrites is not null)
        {
            entry["tryWriteFiles"] = launch.TryWrites;
        }

        if (launch.HomeFiles is not null)
        {
            entry["homeFiles"] = launch.HomeFiles;
        }

        if (launch.Reads is not null)
        {
            entry["readFiles"] = launch.Reads;
        }

        if (launch.IgnoreTerm)
        {
            entry["ignoreTerm"] = true;
        }

        if (launch.Stderr.Length > 0)
        {
            entry["stderr"] = launch.Stderr;
        }

        if (launch.OutfileText.Length > 0)
        {
            entry["outfileText"] = launch.OutfileText;
        }

        return entry;
    }

    /// <summary>The recorded envelope names session 00000000-…; a launch that names another says it by this replacement of the one field.</summary>
    private static string WithSession(string stdout, string session) =>
        session.Length == 0 ? stdout : stdout.Replace("\"session_id\":\"00000000-0000-0000-0000-000000000000\"", $"\"session_id\":\"{session}\"", StringComparison.Ordinal);

    internal CoderRig Says(string model, IReadOnlyDictionary<string, string>? edits, string answer, int sleepMs = 0) => Says(model, new Launch(edits, answer, sleepMs));

    /// <summary>A report that answers every dod id, <paramref name="met"/> or not.</summary>
    internal static string Report(bool met = true, string notes = "done") =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["status"] = "ok",
            ["dod"] = Dod.Select(id => new Dictionary<string, object> { ["id"] = id, ["met"] = met, ["evidence"] = $"{id} is shown by a test" }).ToArray(),
            ["notes"] = notes,
        });

    internal static IReadOnlyDictionary<string, string> Edits(params string[] names) => names.ToDictionary(name => name, name => $"the coder wrote {name}\n");

    internal RoleDescriptor Descriptor(Action<string>? boundary = null) =>
        new(RoleDescriptors.Coder, "the coder", () => new CoderWork { Boundary = boundary, Vendors = Vendors, Turns = TurnsOf(Host, Degrade, OnLauncher), AttemptTimeout = AttemptTimeout, Ladder = [TimeSpan.FromMinutes(5)] }, RouteAs: "coder", Check: CoderTask.Refusal);

    /// <summary>The job's turns over the stand-in machine: the real boundary code, the host and the degrade this rig holds at the moment the descriptor is made.</summary>
    private static Func<IProcessLauncher, CoderTurns> TurnsOf(BoundaryHost host, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade, Func<IProcessLauncher, IProcessLauncher> wrap) =>
        launcher => new CoderTurns(new CoderBoundary(wrap(launcher), host) { Degrade = degrade }, new FilesystemInvariant(launcher));

    internal RoleJobRecord NewJob(string baseSha = "", IReadOnlyList<string>? route = null, int timeoutMinutes = 5) =>
        World.Store.Create(
            new RoleJobRequest { JobId = RoleJobIds.New(World.Clock), Role = RoleDescriptors.Coder, RepoPath = Repo, TaskFile = TaskFile, BaseSha = baseSha, TimeoutMinutes = timeoutMinutes },
            new RoleJobPlan(route ?? ["claude/sonnet"], 4, false));

    /// <summary>The folder of one job's own files — the coder's home, temp folders and caches — where the layer makes them.</summary>
    internal string JobFolder(RoleJobRecord job) => Path.Combine(_jobsFolder ?? _jobs.Path, job.JobId);

    private string? _jobsFolder;

    /// <summary>A folder of the test's own for the job folders (the real-run tests need one outside <c>/tmp</c>, where a job's folder can be covered); the layer is told, and <see cref="JobFolder"/> follows.</summary>
    internal void UseJobsFolder(string folder)
    {
        _jobsFolder = folder;
        Directory.CreateDirectory(folder);
        _inner.Setting(CoderOsLayer.JobsVariable, folder);
    }

    /// <summary>A test-only wrap of the launcher every unit of the job is started through (a real-run test counts and records the turns there); null leaves it as it is.</summary>
    internal Func<IProcessLauncher, IProcessLauncher> OnLauncher { get; set; } = launcher => launcher;

    /// <summary>The vendors a rig stages its fake for: each has its own <c>/tmp</c> (pass 5, M13).</summary>
    internal static readonly string[] StagedVendors = ["claude", "codex", "agy"];

    /// <summary>What <paramref name="vendor"/>'s coder unit sees as <c>/tmp</c>: that vendor's own temp folder of the job (claude's unless said).</summary>
    internal string JobTemp(RoleJobRecord job, string vendor = "claude") => Path.Combine(JobFolder(job), "coder-temp", vendor, "tmp");

    /// <summary>The record folders of the launches of the job, one for each vendor that ran (the fake records inside its own unit's <c>/tmp</c>).</summary>
    internal IReadOnlyList<string> LaunchFolders(RoleJobRecord job) =>
        [.. StagedVendors.Select(vendor => Path.Combine(JobTemp(job, vendor), "launches")).Where(Directory.Exists)];

    /// <summary>
    /// True for a scenario whose coder is codex (epoch 3 E3 s5 t4): its unit covers <c>/tmp</c> with a tmpfs of its own and binds the codex <c>vartmp</c> folder of the job at <c>/var/tmp</c>, so the fake's map and record folder are staged THERE, not in the temp folder a vendor's unit sees as <c>/tmp</c>.
    /// </summary>
    internal bool CodexUnit { get; set; }

    /// <summary>The folder of the job the fake's map and launch records are staged in, as the host sees it.</summary>
    internal string Scratch(RoleJobRecord job) => CodexUnit ? Path.Combine(JobFolder(job), "coder-temp", "codex", "vartmp") : JobTemp(job);

    private string UnitScratch => CodexUnit ? BoundaryUnit.UnitVarTemp : BoundaryUnit.UnitTemp;

    /// <summary>The record folder of the job's launches, as the host sees it: one <c>.argv</c> file for each launch, and the sidecars the fake writes beside them.</summary>
    internal string LaunchFolder(RoleJobRecord job) => Path.Combine(Scratch(job), "launches");

    /// <summary>What FakeCli does for <paramref name="model"/>, launch by launch, as raw model-map entries (a codex-shaped answer the claude envelope of <see cref="Says(string, Launch[])"/> would spoil). The steering file is written again.</summary>
    internal CoderRig SaysRaw(string model, params object[] entries)
    {
        _inner.Says(model, entries);
        Steer(_inner.MapFile, _inner.RecordFolder);

        return this;
    }

    /// <summary>
    /// Makes the job's model map and record folder where the coder's unit will find them: the map is copied into the job's temp folder, and the steering file names the paths AS THE UNIT SEES THEM (<c>/tmp/…</c>). Called by <see cref="RunAsync"/>
    /// just before the job runs; it needs the job id, which a test only has once the job exists.
    /// </summary>
    internal void Stage(RoleJobRecord job)
    {
        foreach (var vendor in CodexUnit ? new[] { "codex" } : StagedVendors)
        {
            var temp = CodexUnit ? Scratch(job) : JobTemp(job, vendor);
            Directory.CreateDirectory(Path.Combine(temp, "launches"));
            if (File.Exists(_inner.MapFile))
            {
                File.Copy(_inner.MapFile, Path.Combine(temp, "model-map.json"), overwrite: true);
            }
        }

        Steer($"{UnitScratch}/model-map.json", $"{UnitScratch}/launches");
        _staged.Add(job.JobId);
    }

    /// <summary>The argv of every launch of the coder of <paramref name="job"/> — recorded inside its unit, read from the job's temp folder; empty when the job never launched a coder.</summary>
    internal IReadOnlyList<string[]> Launches(RoleJobRecord job) =>
    [
        .. (CodexUnit ? new[] { LaunchFolder(job) } : (IEnumerable<string>)LaunchFolders(job)).Where(Directory.Exists).SelectMany(folder => new DirectoryInfo(folder).EnumerateFiles("*.argv")).OrderBy(file => file.CreationTimeUtc).ThenBy(file => file.LastWriteTimeUtc)
            .Select(file => LaunchRecords.Read(file.FullName).Split('\0')),
    ];

    internal async Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleJobRecord job, Action<string>? boundary = null, TimeProvider? clock = null)
    {
        Stage(job);
        if (clock is null)
        {
            return await _inner.RunAsync(job, [Descriptor(boundary)]);
        }

        var exit = await _inner.RunnerWith([Descriptor(boundary)], clock).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        return (exit, World.Store.Read(job.JobId)!);
    }

    internal JsonElement Envelope(RoleJobRecord job) => JsonDocument.Parse(World.Store.FolderOf(job.JobId)!.ReadResult()).RootElement.Clone();

    internal string WorktreeOf(RoleJobRecord job) => Path.Combine(Root, $"coai-coder-{job.JobId}");

    internal async Task<string> GitAsync(string folder, params string[] args)
    {
        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", args, folder));

        return result.ExitCode == 0 ? result.StdOut.Trim() : $"git exited {result.ExitCode}: {result.StdErr.Trim()}";
    }

    /// <summary>The coder units of the jobs this rig ran that are still loaded; empty where there is no systemctl.</summary>
    private async Task<IReadOnlyList<string>> UnitsLeftAsync()
    {
        var left = new List<string>();
        foreach (var id in _staged)
        {
            left.AddRange(await UnitsOfAsync(id));
        }

        return left;
    }

    /// <summary>The units of one job that are loaded now — the coder's (<c>coai-coder-</c>) and its checks' (<c>coai-check-</c>); empty where there is no systemctl at all.</summary>
    internal Task<IReadOnlyList<string>> UnitsOfAsync(RoleJobRecord job) => UnitsOfAsync(job.JobId);

    private Task<IReadOnlyList<string>> UnitsOfAsync(string jobId) => UnitsOfJobAsync(jobId, _root.Path);

    /// <summary>The loaded units of the job <paramref name="jobId"/> — one copy for the in-process rig and the real-binary scenarios; <paramref name="workingFolder"/> is where <c>systemctl</c> is started.</summary>
    internal static async Task<IReadOnlyList<string>> UnitsOfJobAsync(string jobId, string workingFolder)
    {
        try
        {
            var listed = await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "list-units", $"coai-coder-{jobId}-*", $"coai-check-{jobId}-*", "--all", "--no-legend", "--plain"], workingFolder) { Timeout = TimeSpan.FromSeconds(20) });

            return [.. listed.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0])];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No systemctl at all: no unit can have been started.
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        var left = await UnitsLeftAsync();
        foreach (var unit in left)
        {
            await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "stop", unit], _root.Path) { Timeout = TimeSpan.FromSeconds(20) });
        }

        _steering.Dispose();
        await _inner.DisposeAsync();
        _root.Dispose();
        _jobs.Dispose();
        _home.Dispose();
        left.Should().BeEmpty("a coder test must stop every unit it starts");
    }
}
