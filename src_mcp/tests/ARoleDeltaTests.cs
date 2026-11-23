using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s4 t3 — <c>--role-delta &lt;jobId&gt; --at &lt;commit&gt;</c>: <c>changedShare = (changed + deleted + added task files) / manifest task files</c>, passing below 0.20, read from a real git repository and a real job folder.
/// Of ten task files one edited is 0.10 and passes; one edited and one deleted is 0.20 and fails; an added task counts; a change of line ends alone does not. Exits 0, 65, 74 — never 64.
/// </summary>
public sealed class ARoleDeltaTests : IAsyncDisposable
{
    private const string Epic = "plan/epoch-1/e1-core";

    private readonly RoleJobWorld _world = new();
    private TempGitRepo? _repo;
    private RoleJobRecord? _job;

    private static string TaskPath(int number) => $"{Epic}/stories/s1-first/tasks/t{number}-part/task.yaml";

    private static string TaskText(int number) => $"id: t{number}-part\ntitle: \"Part {number}\"\nkind: code\ncomplexity: 3\n";

    private static string StoryPath(int number) => $"{Epic}/stories/s{number}-x/story.yaml";

    private TempGitRepo Repo => _repo!;

    /// <summary>Ten task files and two story files committed, and a succeeded BA job whose manifest names exactly them.</summary>
    private async Task<string> SetUpAsync(int tasks = 10, string role = RoleDescriptors.Ba, string state = RoleJobStates.Succeeded)
    {
        _repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-delta-repo-");
        await Repo.GitAsync("config", "core.autocrlf", "false");
        var entries = new List<BaManifestEntry>();
        foreach (var number in Enumerable.Range(1, tasks))
        {
            Put(TaskPath(number), TaskText(number));
            entries.Add(new BaManifestEntry(TaskPath(number), BaPublisher.Sha256Of(TaskText(number)), "task"));
        }

        foreach (var number in new[] { 1, 2 })
        {
            Put(StoryPath(number), $"id: s{number}-x\n");
            entries.Add(new BaManifestEntry(StoryPath(number), BaPublisher.Sha256Of($"id: s{number}-x\n"), "story"));
        }

        await Repo.CommitAsync("the BA's plan");
        _job = _world.Store.Create(new RoleJobRequest { JobId = RoleJobIds.New(_world.Clock), Role = role, RepoPath = Repo.Path, TaskFile = $"{Epic}/epic.yaml", TimeoutMinutes = 5 });
        File.WriteAllText(Path.Combine(_world.Store.FolderOf(_job.JobId)!.Location, BaPublisher.ManifestName), BaPublisher.ManifestText(entries));
        _world.Store.Change(_job.JobId, job => job with { State = state });

        return await Repo.HeadAsync();
    }

    private void Put(string relative, string text)
    {
        var path = Path.Combine(Repo.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task<string> CommitAsync(string message)
    {
        await Repo.CommitAsync(message);

        return await Repo.HeadAsync();
    }

    private async Task<(int Code, JsonElement Answer, string Refusal)> MeasureAsync(string commit, string? jobId = null)
    {
        var outcome = await RoleDeltaMode.EvaluateAsync(jobId ?? _job!.JobId, commit, _world.DataDir, new ProcessLauncher(), TestContext.Current.CancellationToken);

        return (outcome.Code, outcome.Json.Length > 0 ? JsonDocument.Parse(outcome.Json).RootElement.Clone() : default, outcome.Refusal);
    }

    // ---------- d1: the share ----------

    [Fact]
    public async Task D1_TenTaskFiles_OneEdited_IsPointOneZero_AndPasses()
    {
        await SetUpAsync();
        Put(TaskPath(4), TaskText(4) + "dependsOn: []\n");
        var commit = await CommitAsync("the orchestrator edits one task");

        var (code, answer, refusal) = await MeasureAsync(commit);

        code.Should().Be(0, refusal);
        (answer.GetProperty("manifestTasks").GetInt32(), answer.GetProperty("changed").GetInt32(), answer.GetProperty("deleted").GetInt32(), answer.GetProperty("added").GetInt32()).Should().Be((10, 1, 0, 0));
        answer.GetProperty("changedShare").GetDouble().Should().Be(0.1);
        answer.GetProperty("pass").GetBoolean().Should().BeTrue();
        answer.GetProperty("changedFiles").EnumerateArray().Select(file => file.GetString()).Should().Equal(TaskPath(4));
        answer.GetProperty("mode").GetString().Should().Be("role-delta");
    }

    [Fact]
    public async Task D1_OneEditedAndOneDeleted_IsPointTwoZero_AndFails()
    {
        await SetUpAsync();
        Put(TaskPath(4), TaskText(4) + "dependsOn: []\n");
        File.Delete(Path.Combine(Repo.Path, TaskPath(7).Replace('/', Path.DirectorySeparatorChar)));
        var commit = await CommitAsync("one edited, one dropped");

        var (code, answer, _) = await MeasureAsync(commit);

        code.Should().Be(0, "a share over the line is still a measurement: the answer says pass: false");
        (answer.GetProperty("changed").GetInt32(), answer.GetProperty("deleted").GetInt32()).Should().Be((1, 1));
        answer.GetProperty("changedShare").GetDouble().Should().Be(0.2);
        answer.GetProperty("pass").GetBoolean().Should().BeFalse("it passes BELOW 0.20, and 0.20 is not below it");
        answer.GetProperty("deletedFiles")[0].GetString().Should().Be(TaskPath(7));
    }

    [Fact]
    public async Task D1_AnAddedTask_Counts_AndTwoAddedOnesFail()
    {
        await SetUpAsync();
        Put($"{Epic}/stories/s2-x/tasks/t1-extra/task.yaml", "id: t1-extra\n");
        var one = await CommitAsync("one more task");
        (await MeasureAsync(one)).Answer.GetProperty("added").GetInt32().Should().Be(1);
        (await MeasureAsync(one)).Answer.GetProperty("pass").GetBoolean().Should().BeTrue("one in ten is 0.10");

        Put($"{Epic}/stories/s2-x/tasks/t2-extra/task.yaml", "id: t2-extra\n");
        var two = await CommitAsync("two more tasks");

        var (_, answer, _) = await MeasureAsync(two);
        answer.GetProperty("added").GetInt32().Should().Be(2);
        answer.GetProperty("changedShare").GetDouble().Should().Be(0.2);
        answer.GetProperty("pass").GetBoolean().Should().BeFalse();
        answer.GetProperty("addedFiles").EnumerateArray().Select(file => file.GetString()).Should().Equal($"{Epic}/stories/s2-x/tasks/t1-extra/task.yaml", $"{Epic}/stories/s2-x/tasks/t2-extra/task.yaml");
    }

    [Fact]
    public async Task D1_ACrLfOnlyChange_DoesNotCount()
    {
        await SetUpAsync();
        foreach (var number in Enumerable.Range(1, 10))
        {
            Put(TaskPath(number), TaskText(number).Replace("\n", "\r\n", StringComparison.Ordinal));
        }

        var commit = await CommitAsync("every task file checked out with CRLF");
        (await Repo.RunAsync("diff", "--stat", "HEAD~1", "HEAD")).StdOut.Should().Contain("10 files changed", "git sees the line ends as a change; the measure does not");

        var (code, answer, _) = await MeasureAsync(commit);

        code.Should().Be(0);
        (answer.GetProperty("changed").GetInt32(), answer.GetProperty("deleted").GetInt32(), answer.GetProperty("added").GetInt32()).Should().Be((0, 0, 0));
        answer.GetProperty("changedShare").GetDouble().Should().Be(0);
        answer.GetProperty("pass").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AStoryFileChange_IsNotATaskFileChange_AndARenameIsADeleteAndAnAdd()
    {
        await SetUpAsync();
        Put(StoryPath(1), "id: s1-x\ntitle: \"edited\"\n");
        var story = await CommitAsync("a story edited");
        (await MeasureAsync(story)).Answer.GetProperty("changedShare").GetDouble().Should().Be(0, "the measure is of tasks");

        Directory.CreateDirectory(Path.Combine(Repo.Path, $"{Epic}/stories/s1-first/tasks/t3-renamed".Replace('/', Path.DirectorySeparatorChar)));
        await Repo.GitAsync("mv", TaskPath(3), $"{Epic}/stories/s1-first/tasks/t3-renamed/task.yaml");
        var renamed = await CommitAsync("a task folder renamed");

        var (_, answer, _) = await MeasureAsync(renamed);
        (answer.GetProperty("deleted").GetInt32(), answer.GetProperty("added").GetInt32()).Should().Be((1, 1));
        answer.GetProperty("pass").GetBoolean().Should().BeFalse("two in ten");
    }

    [Fact]
    public async Task AnEarlierCommit_IsMeasuredAsItWas_NotAsTheTreeIsNow()
    {
        var first = await SetUpAsync();
        Put(TaskPath(1), "id: rewritten\n");
        await CommitAsync("later change");

        var (_, answer, _) = await MeasureAsync(first);

        answer.GetProperty("changed").GetInt32().Should().Be(0);
    }

    // ---------- d2: exits 0, 65, 74 — never 64 ----------

    [Fact]
    public async Task D2_TheRealRunner_ExitsZero_OnAMeasurement_AndPrintsOneObject()
    {
        var head = await SetUpAsync();
        var output = new StringWriter();

        var code = await RoleDeltaMode.RunAsync(["--role-delta", _job!.JobId, "--at", head], Env, output, _ => { }, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        JsonDocument.Parse(output.ToString()).RootElement.GetProperty("pass").GetBoolean().Should().BeTrue();
        output.ToString().TrimEnd().Split('\n').Should().ContainSingle("ONE JSON object, one line");
    }

    private Func<string, string?> Env => name => name == "COAI_DATA_DIR" ? _world.DataDir : null;

    public static TheoryData<string[]> WrongArguments =>
    [
        new[] { "--role-delta" },
        new[] { "--role-delta", "r20260101000000-aaaaaaaa" },
        new[] { "--role-delta", "r20260101000000-aaaaaaaa", "--at" },
        new[] { "--role-delta", "r20260101000000-aaaaaaaa", "--commit", "HEAD" },
        new[] { "--role-delta", "../../etc", "--at", "HEAD" },
        new[] { "--role-delta", "r20260101000000-aaaaaaaa", "--at", "--output=elsewhere" },
        new[] { "--role-delta", "r20260101000000-aaaaaaaa", "--at", "has space" },
        new[] { "--role-delta", "r20260101000000-aaaaaaaa", "--at", "HEAD", "extra" },
        new[] { "--role-delta", "--at", "r20260101000000-aaaaaaaa", "HEAD" },
    ];

    [Theory]
    [MemberData(nameof(WrongArguments))]
    public async Task D2_AWrongArgument_Is65_NeverSixtyFour(string[] args)
    {
        var said = new List<string>();

        var code = await RoleDeltaMode.RunAsync(args, Env, new StringWriter(), said.Add, TestContext.Current.CancellationToken);

        code.Should().Be(65);
        said.Should().ContainSingle().Which.Should().Contain("--role-delta takes a job id and a commit");
    }

    [Fact]
    public async Task D2_TheModeIsKnown_SoTheDispatchNeverAnswersSixtyFour()
    {
        Program.Classify(["--role-delta"]).Should().Be(Program.Startup.RoleDelta);
        Program.Classify(["--role-delta", "x", "--at", "y"]).Should().NotBe(Program.Startup.Usage);
        Program.WriterModes.Should().NotContain(Program.Startup.RoleDelta, "it is a reader: it never makes the data directory");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task D2_NoSuchJob_NotABaJob_ANotSucceededJob_AreEach65_WithTheirOwnSentence()
    {
        var head = await SetUpAsync();

        var (noJob, _, noJobSaid) = await MeasureAsync(head, "r20260101000000-aaaaaaaa");
        noJob.Should().Be(65);
        noJobSaid.Should().Contain("there is no role job");

        var probe = _world.NewJob();
        _world.Store.Change(probe.JobId, job => job with { State = RoleJobStates.Succeeded });
        var (notBa, _, notBaSaid) = await MeasureAsync(head, probe.JobId);
        notBa.Should().Be(65);
        notBaSaid.Should().Contain("probe job").And.Contain("BA job that succeeded");

        var running = _world.Store.Create(new RoleJobRequest { JobId = RoleJobIds.New(_world.Clock), Role = RoleDescriptors.Ba, RepoPath = Repo.Path, TaskFile = $"{Epic}/epic.yaml", TimeoutMinutes = 5 });
        var (notDone, _, notDoneSaid) = await MeasureAsync(head, running.JobId);
        notDone.Should().Be(65);
        notDoneSaid.Should().Contain("state queued");
    }

    [Fact]
    public async Task D2_AnUnknownCommit_ARepositoryThatIsGone_AndAJobWithNoManifest_AreEach65()
    {
        var head = await SetUpAsync();

        var (unknown, _, unknownSaid) = await MeasureAsync("0123456789abcdef0123456789abcdef01234567");
        unknown.Should().Be(65);
        unknownSaid.Should().Contain("is not a commit");

        var (branch, _, _) = await MeasureAsync("no-such-branch");
        branch.Should().Be(65);

        File.Delete(Path.Combine(_world.Store.FolderOf(_job!.JobId)!.Location, BaPublisher.ManifestName));
        var (noManifest, _, noManifestSaid) = await MeasureAsync(head);
        noManifest.Should().Be(65);
        noManifestSaid.Should().Contain("no ba-manifest.json");

        var gone = _world.Store.Create(new RoleJobRequest { JobId = RoleJobIds.New(_world.Clock), Role = RoleDescriptors.Ba, RepoPath = Path.Combine(Repo.Path, "no-such-folder"), TaskFile = $"{Epic}/epic.yaml", TimeoutMinutes = 5 });
        File.WriteAllText(Path.Combine(_world.Store.FolderOf(gone.JobId)!.Location, BaPublisher.ManifestName), BaPublisher.ManifestText([new BaManifestEntry(TaskPath(1), "00", "task")]));
        _world.Store.Change(gone.JobId, job => job with { State = RoleJobStates.Succeeded });
        var (noRepo, _, noRepoSaid) = await MeasureAsync(head, gone.JobId);
        noRepo.Should().Be(65);
        noRepoSaid.Should().Contain("not a folder any more");
    }

    [Fact]
    public async Task D2_ATornManifest_Is74_AndAManifestWithNoTaskIs65()
    {
        var head = await SetUpAsync();
        var manifest = Path.Combine(_world.Store.FolderOf(_job!.JobId)!.Location, BaPublisher.ManifestName);

        File.WriteAllText(manifest, "{\"files\": [ {\"path\": ");
        var (torn, _, tornSaid) = await MeasureAsync(head);
        torn.Should().Be(74);
        tornSaid.Should().Contain("could not be read");

        File.WriteAllText(manifest, BaPublisher.ManifestText([new BaManifestEntry(StoryPath(1), "00", "story")]));
        var (noTasks, _, noTasksSaid) = await MeasureAsync(head);
        noTasks.Should().Be(65);
        noTasksSaid.Should().Contain("names no task file");
    }

    [Fact]
    public async Task D2_AFolderThatIsNoRepository_Is65_AndAGitThatCannotRun_Is74()
    {
        var head = await SetUpAsync();
        using var notGit = TempDir.For("coai-delta-nogit-");
        var plain = _world.Store.Create(new RoleJobRequest { JobId = RoleJobIds.New(_world.Clock), Role = RoleDescriptors.Ba, RepoPath = notGit, TaskFile = $"{Epic}/epic.yaml", TimeoutMinutes = 5 });
        File.WriteAllText(Path.Combine(_world.Store.FolderOf(plain.JobId)!.Location, BaPublisher.ManifestName), BaPublisher.ManifestText([new BaManifestEntry(TaskPath(1), "00", "task")]));
        _world.Store.Change(plain.JobId, job => job with { State = RoleJobStates.Succeeded });

        var (notRepo, _, notRepoSaid) = await MeasureAsync(head, plain.JobId);
        notRepo.Should().Be(65);
        notRepoSaid.Should().Contain("not a git repository");

        var outcome = await RoleDeltaMode.EvaluateAsync(_job!.JobId, head, _world.DataDir, new CannotStartGit(), TestContext.Current.CancellationToken);
        outcome.Code.Should().Be(74);
        outcome.Refusal.Should().Contain("git could not be started");
    }

    private sealed class CannotStartGit : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => throw new System.ComponentModel.Win32Exception("no such file");
    }

    public async ValueTask DisposeAsync()
    {
        _world.Dispose();
        if (_repo is not null)
        {
            await _repo.DisposeAsync();
        }
    }
}
