using System.Text.Json;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
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
    private IDisposable _steering = FakeCliSteering.Unwritten;

    private CoderRig(RoleFailoverRig inner)
    {
        _inner = inner;
        _inner.Setting("COAI_CODER_ROOT", _root.Path);
    }

    internal RoleFailoverRig Rig => _inner;

    internal string Repo => _inner.Repo;

    internal string Root => _root.Path;

    internal RoleJobWorld World => _inner.World;

    /// <summary>The commit the repository is on when the scenario starts — what a coder's base is when the request names none.</summary>
    internal string Base { get; private set; } = string.Empty;

    internal static async Task<CoderRig> CreateAsync()
    {
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

    /// <summary>One launch of the fake coder: the files it writes below its working folder, what it says, and how long it waits before it does.</summary>
    internal sealed record Launch(IReadOnlyDictionary<string, string>? Edits, string Answer, int SleepMs = 0);

    /// <summary>
    /// What FakeCli does for <paramref name="model"/>, launch by launch (the last repeats). The steering file — the coder's child reads only that — is written again with every call.
    /// </summary>
    internal CoderRig Says(string model, params Launch[] launches)
    {
        _inner.Says(model, [.. launches.Select(launch => (object)Entry(launch))]);
        _steering.Dispose();
        _steering = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_MODEL_MAP"] = _inner.MapFile, ["FAKECLI_RECORD_DIR"] = _inner.RecordFolder });

        return this;
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
        new(RoleDescriptors.Coder, "the coder", () => new CoderWork { Boundary = boundary, AttemptTimeout = TimeSpan.FromSeconds(30), Ladder = [TimeSpan.FromMinutes(5)] }, RouteAs: "coder", Check: CoderTask.Refusal);

    internal RoleJobRecord NewJob(string baseSha = "", IReadOnlyList<string>? route = null, int timeoutMinutes = 5) =>
        World.Store.Create(
            new RoleJobRequest { JobId = RoleJobIds.New(World.Clock), Role = RoleDescriptors.Coder, RepoPath = Repo, TaskFile = TaskFile, BaseSha = baseSha, TimeoutMinutes = timeoutMinutes },
            new RoleJobPlan(route ?? ["claude/sonnet"], 4, false));

    internal Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleJobRecord job, Action<string>? boundary = null) => _inner.RunAsync(job, [Descriptor(boundary)]);

    internal JsonElement Envelope(RoleJobRecord job) => JsonDocument.Parse(World.Store.FolderOf(job.JobId)!.ReadResult()).RootElement.Clone();

    internal string WorktreeOf(RoleJobRecord job) => Path.Combine(Root, $"coai-coder-{job.JobId}");

    internal async Task<string> GitAsync(string folder, params string[] args)
    {
        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", args, folder));

        return result.ExitCode == 0 ? result.StdOut.Trim() : $"git exited {result.ExitCode}: {result.StdErr.Trim()}";
    }

    public async ValueTask DisposeAsync()
    {
        _steering.Dispose();
        await _inner.DisposeAsync();
        _root.Dispose();
    }
}
