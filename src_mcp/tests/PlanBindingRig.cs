using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One world for the plan binding scenarios (epoch 3 E2 s5): a real git repository on the branch <c>feature</c> holding a small work plan (one task) and a committed plan file, the review service over the code-round fixture
/// (FakeCli reviewers answering a clean review — no model is called), a data directory shared by the review sessions and the coder jobs, and a way to start a coder job through the real <c>start_role</c> service whose runner is
/// never launched. The coder's own work is not run: the scenario makes the branch <c>coder/&lt;jobId&gt;</c> itself, one commit above the job's base.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection: the fixture steers the fake through process-wide environment variables.</remarks>
[Collection("fakecli-env")]
internal sealed class PlanBindingRig : IAsyncDisposable
{
    internal const string Story = "plan/epoch-1/e1-core/stories/s1-first";

    internal const string TaskFile = $"{Story}/tasks/t1-a/task.yaml";

    internal const string PlanFile = "plan/PLAN.md";

    internal const string PlanText = "# The plan\n\nGoal: the thing is written, tested and proven by a test, so that the feature can name it. What must be true when it is done: src/thing.cs exists, a test names it, and the rounds that review it judge it against this text.\n\nConstraints: no new dependency, and the launcher stays the one launcher of the build.\n";

    private readonly CodeRoundHarness _h = new();

    private PlanBindingRig()
    {
    }

    internal string Repo => _h.Repo;

    internal string Data => _h.Data;

    internal PanelService Service { get; private set; } = null!;

    internal RoleJobStore Jobs => new(Data, TimeProvider.System, Serilog.Core.Logger.None);

    /// <param name="threshold">The gate threshold of the review service: 0 makes a round with one major finding `revise`, which leaves a finding to resolve.</param>
    internal static async Task<PlanBindingRig> CreateAsync(int threshold = 2)
    {
        var rig = new PlanBindingRig();
        await rig._h.InitializeAsync();
        rig.Service = rig._h.Service(threshold: threshold);
        BaScenario.Lay(rig.Repo);
        BaScenario.Write(rig.Repo, $"{Story}/story.yaml", WorkPlanYaml.Story(new BaStory("s1-first", "A story", [], [])));
        BaScenario.Write(rig.Repo, TaskFile, WorkPlanYaml.Task(new BaTask("t1-a", "Write the thing", "code", 4, [], false, ["src/thing.cs"], [new BaDod("d1", "the thing works")], null)));
        BaScenario.Write(rig.Repo, PlanFile, PlanText);
        await rig.CommitAsync("the plan and its task");

        return rig;
    }

    internal async Task<string> CommitAsync(string message)
    {
        await _h.Git("add", "-A");
        await _h.Git("commit", "-m", message);

        return await HeadAsync();
    }

    internal async Task<string> HeadAsync() => (await GitOutAsync("rev-parse", "HEAD"));

    internal async Task<string> GitOutAsync(params string[] args)
    {
        var result = await _h.Launcher.RunAsync(new ProcessRequest("git", args, Repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }

    internal Task GitAsync(params string[] args) => _h.Git(args);

    internal static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>The feature's own session takes the plan through the plan gate to <c>proceed</c>, from the committed file; answers its session id.</summary>
    internal async Task<string> ApproveAsync(string branch = "feature", string planFile = PlanFile)
    {
        var opened = Parse(await Service.OpenAsync(Repo, branch));
        var planned = Parse(await Service.ReviewPlanAsync(Repo, branch, new PlanInput(string.Empty, planFile, string.Empty)));
        planned.GetProperty("verdict").GetString().Should().Be("proceed", planned.ToString());
        Parse(await Service.ResolveAsync(Repo, branch, "[]")).GetProperty("stage").GetString().Should().Be("CodeReview");

        return opened.GetProperty("sessionId").GetString()!;
    }

    /// <summary>Starts a coder job through the real service (its runner is never launched); answers the job's id, or the refusal's text as <c>error:…</c>.</summary>
    internal async Task<string> StartJobAsync(string? baseSha = null, string taskFile = TaskFile)
    {
        var service = new RoleJobService(Data, name => name == "COAI_ROLE_LAUNCHER" ? "setsid" : null, Serilog.Core.Logger.None, _ => true)
        {
            Launcher = new DetachedLauncher(new NothingStarted()),
        };
        var answer = Parse(await service.StartAsync(RoleDescriptors.Coder, Repo, taskFile, null, null, null, null, baseSha, TestContext.Current.CancellationToken));

        return answer.TryGetProperty("jobId", out var id) ? id.GetString()! : "error:" + answer.GetProperty("error").GetString();
    }

    /// <summary>The branch a coder job makes — <c>coder/&lt;jobId&gt;</c>, one commit above <paramref name="baseSha"/> — made here instead of by a coder; the checkout is left where it was.</summary>
    internal async Task<string> MakeCoderBranchAsync(string jobId, string baseSha, string file = "src/thing.cs")
    {
        var back = await GitOutAsync("rev-parse", "--abbrev-ref", "HEAD");
        await GitAsync("checkout", "-b", $"coder/{jobId}", baseSha);
        BaScenario.Write(Repo, file, $"the coder wrote {file} for {jobId}\n");
        var head = await CommitAsync($"coder {jobId}");
        await GitAsync("checkout", back);

        return head;
    }

    /// <summary>One more commit on a branch (the checkout is left where it was) — what a coder does between two code rounds.</summary>
    internal async Task<string> CommitOnAsync(string branch, string file)
    {
        var back = await GitOutAsync("rev-parse", "--abbrev-ref", "HEAD");
        await GitAsync("checkout", branch);
        BaScenario.Write(Repo, file, $"another commit that wrote {file}\n");
        var head = await CommitAsync($"more work in {file}");
        await GitAsync("checkout", back);

        return head;
    }

    /// <summary>Everything about a session that a round or a resolve could move — what a sibling's work must leave as it was.</summary>
    internal static (string Id, Stage Stage, int Rounds, int Run, bool Awaiting, bool Proceeded, int Rejections, int Pending, string PlanText, string Inherited) Snapshot(PersistedSession session) =>
        (session.State.SessionId, session.State.Stage, session.Rounds.Count, session.State.RoundsRunThisStage, session.State.AwaitingResolve, session.State.PlanProceeded, session.State.Rejections.Length, session.Pending.Count, session.PlanText, session.State.Inherited?.ToString() ?? string.Empty);

    internal (string Id, Stage Stage, int Rounds, int Run, bool Awaiting, bool Proceeded, int Rejections, int Pending, string PlanText, string Inherited) SnapshotOf(string branch) =>
        Snapshot(new SessionStore(Data).Load(Repo, branch)!);

    /// <summary>Starts a job from the current branch, makes its branch, and answers its id.</summary>
    internal async Task<string> JobWithBranchAsync(string? baseSha = null)
    {
        var jobId = await StartJobAsync(baseSha);
        jobId.Should().NotStartWith("error:");
        await MakeCoderBranchAsync(jobId, Jobs.ReadRequest(jobId)!.BaseSha);

        return jobId;
    }

    /// <summary>A code round on the job's branch, sent no scope of its own — the plan it inherits is the scope. The session is opened first (idempotent), as a caller does.</summary>
    internal async Task<JsonElement> ReviewCodeAsync(string jobId, bool again = false, string scope = "")
    {
        await Service.OpenAsync(Repo, $"coder/{jobId}");

        return Parse(await Service.ReviewCodeAsync(Repo, $"coder/{jobId}", "feature", scope, again, TestContext.Current.CancellationToken));
    }

    internal sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    public async ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await _h.DisposeAsync();
    }
}
