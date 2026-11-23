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
    internal BoundaryHost Host => OnHost(BoundaryHost.Current() with { RealHome = _home.Path, NuGetPackages = string.Empty, Path = string.Empty });

    /// <summary>A test-only change of the machine (no user manager, another operating system) — read when the job's work is made.</summary>
    internal Func<BoundaryHost, BoundaryHost> OnHost { get; set; } = host => host;

    /// <summary>A test-only rewrite of every unit's argument list (a property taken away); null leaves the layer as it ships.</summary>
    internal Func<IReadOnlyList<string>, IReadOnlyList<string>>? Degrade { get; set; }

    /// <summary>
    /// The rig. A coder launch needs a systemd user manager, so without one the test is SKIPPED, with the reason — unless <paramref name="needsLayer"/> is false, for a scenario in which no coder is ever launched.
    /// </summary>
    internal static async Task<CoderRig> CreateAsync(bool needsLayer = true)
    {
        if (needsLayer)
        {
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
        }

        var rig = new CoderRig(await RoleFailoverRig.CreateAsync());
        BaScenario.Lay(rig.Repo);
        BaScenario.Write(rig.Repo, $"{Story}/story.yaml", WorkPlanYaml.Story(new BaStory("s1-first", "A story", [], [])));
        BaScenario.Write(rig.Repo, TaskFile, WorkPlanYaml.Task(new BaTask(TaskId, "Write the thing", "code", 4, [], false, ["src/thing.cs"], [new BaDod("d1", "the thing works"), new BaDod("d2", "the thing is tested")], null)));
        foreach (var (key, value) in new[] { ("user.email", "t@t"), ("user.name", "t"), ("commit.gpgsign", "false") })
        {
            await rig.Rig.Git.GitAsync("config", key, value);
        }

        await rig.Rig.Git.CommitAsync("the plan and its task");
        rig.Base = await rig.Rig.Git.HeadAsync();
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "2.1.330 (Claude Code)\n");
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "help-2.1.330-wsl.txt")));

        return rig;
    }

    /// <summary>
    /// One launch of the fake coder: the files it writes below its working folder, what it says, how long it waits before it answers, and — a coder that goes where it must not — the ABSOLUTE paths it tries to write (what the OS refuses
    /// goes to its stderr).
    /// </summary>
    internal sealed record Launch(IReadOnlyDictionary<string, string>? Edits, string Answer, int SleepMs = 0, IReadOnlyList<string>? TryWrites = null);

    /// <summary>
    /// What FakeCli does for <paramref name="model"/>, launch by launch (the last repeats). The steering file — the coder's child reads only that — is written again with every call.
    /// </summary>
    internal CoderRig Says(string model, params Launch[] launches)
    {
        _inner.Says(model, [.. launches.Select(launch => (object)Entry(launch))]);
        Steer(_inner.MapFile, _inner.RecordFolder);

        return this;
    }

    private void Steer(string map, string record)
    {
        _steering.Dispose();
        _steering = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_MODEL_MAP"] = map, ["FAKECLI_RECORD_DIR"] = record });
    }

    /// <summary>One model-map entry: claude's recorded success envelope around the answer, and — only when there are edits — the <c>writeFiles</c> object (a null would be read by the fake as an edit it cannot make).</summary>
    private static Dictionary<string, object?> Entry(Launch launch)
    {
        var entry = new Dictionary<string, object?>
        {
            ["stdout"] = JsonSerializer.SerializeToElement(FakeRoleAnswers.ClaudeSays(launch.Answer)).GetProperty("stdout").GetString(),
            ["exit"] = 0,
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

        return entry;
    }

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
        new(RoleDescriptors.Coder, "the coder", () => new CoderWork { Boundary = boundary, Turns = TurnsOf(Host, Degrade), AttemptTimeout = TimeSpan.FromSeconds(30), Ladder = [TimeSpan.FromMinutes(5)] }, RouteAs: "coder", Check: CoderTask.Refusal);

    /// <summary>The job's turns over the stand-in machine: the real boundary code, the host and the degrade this rig holds at the moment the descriptor is made.</summary>
    private static Func<IProcessLauncher, CoderTurns> TurnsOf(BoundaryHost host, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade) =>
        launcher => new CoderTurns(new CoderBoundary(launcher, host) { Degrade = degrade }, new FilesystemInvariant(launcher));

    internal RoleJobRecord NewJob(string baseSha = "", IReadOnlyList<string>? route = null, int timeoutMinutes = 5) =>
        World.Store.Create(
            new RoleJobRequest { JobId = RoleJobIds.New(World.Clock), Role = RoleDescriptors.Coder, RepoPath = Repo, TaskFile = TaskFile, BaseSha = baseSha, TimeoutMinutes = timeoutMinutes },
            new RoleJobPlan(route ?? ["claude/sonnet"], 4, false));

    /// <summary>The folder of one job's own files — the coder's home, temp folders and caches — where the layer makes them.</summary>
    internal string JobFolder(RoleJobRecord job) => Path.Combine(_jobs.Path, job.JobId);

    /// <summary>What the coder's unit sees as <c>/tmp</c>: the job's own temp folder.</summary>
    internal string JobTemp(RoleJobRecord job) => Path.Combine(JobFolder(job), "coder", "tmp");

    /// <summary>
    /// Makes the job's model map and record folder where the coder's unit will find them: the map is copied into the job's temp folder, and the steering file names the paths AS THE UNIT SEES THEM (<c>/tmp/…</c>). Called by <see cref="RunAsync"/>
    /// just before the job runs; it needs the job id, which a test only has once the job exists.
    /// </summary>
    internal void Stage(RoleJobRecord job)
    {
        var temp = JobTemp(job);
        Directory.CreateDirectory(Path.Combine(temp, "launches"));
        if (File.Exists(_inner.MapFile))
        {
            File.Copy(_inner.MapFile, Path.Combine(temp, "model-map.json"), overwrite: true);
        }

        Steer($"{BoundaryUnit.UnitTemp}/model-map.json", $"{BoundaryUnit.UnitTemp}/launches");
        _staged.Add(job.JobId);
    }

    /// <summary>The argv of every launch of the coder of <paramref name="job"/> — recorded inside its unit, read from the job's temp folder; empty when the job never launched a coder.</summary>
    internal IReadOnlyList<string[]> Launches(RoleJobRecord job) =>
        Directory.Exists(Path.Combine(JobTemp(job), "launches")) ? RoleFailoverRig.LaunchesIn(Path.Combine(JobTemp(job), "launches")) : [];

    internal async Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleJobRecord job, Action<string>? boundary = null)
    {
        Stage(job);

        return await _inner.RunAsync(job, [Descriptor(boundary)]);
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
            try
            {
                var listed = await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "list-units", $"coai-coder-{id}-*", "--all", "--no-legend", "--plain"], _root.Path) { Timeout = TimeSpan.FromSeconds(20) });
                left.AddRange(listed.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0]));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No systemctl at all: no unit can have been started.
            }
        }

        return left;
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
