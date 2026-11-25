using System.Collections;
using System.Text.Json;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s4 t2 — the BA run (spec C, E.1, E.2). The real runner, launcher, classifier, tree check and plan validator; FakeCli says the BA's answer for each model, so no model is called and nothing is paid:
/// valid at turn 0; valid after ONE repair turn in the SAME session; invalid three times ends <c>output-invalid</c> with ZERO files written; nothing outside the epic folder changes; an epic with a story is refused
/// before a launch.
/// </summary>
[Collection("fakecli-env")]
public sealed class ABaRunTests
{
    private static readonly string[] RouteBoth = ["claude/fable", "claude/opus"];

    private static async Task<RoleFailoverRig> RigAsync()
    {
        var rig = await RoleFailoverRig.CreateAsync();
        BaScenario.Lay(rig.Repo);
        await rig.Git.CommitAsync("the plan folder");

        return rig;
    }

    private static object Says(JsonNodeLike answer) => FakeRoleAnswers.ClaudeSays(answer.Text);

    private static Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleFailoverRig rig, IReadOnlyList<string>? route = null, Action<string>? boundary = null) =>
        rig.RunAsync(rig.NewJob(RoleDescriptors.Ba, route ?? RouteBoth, BaScenario.EpicFile), [BaScenario.Descriptor(boundary)]);

    private static string Stories(RoleFailoverRig rig) => Path.Combine(rig.Repo, BaScenario.EpicFolder, "stories");

    private static string LastPrompt(RoleFailoverRig rig, int launch) => rig.Launches()[launch][^1];

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    [Fact]
    public void TheBaRole_IsRegistered_RoutedAsBa_WithItsOwnLookAtTheRequest()
    {
        var ba = RoleDescriptors.Find(RoleDescriptors.Shipped, "ba");

        ba.Should().NotBeNull();
        ba!.RouteAs.Should().Be("ba");
        ba.Check.Should().NotBeNull();
        RoleDescriptors.Shipped.Select(descriptor => descriptor.Role).Should().Contain(["probe", "ba"]);
    }

    // ---------- d1: valid at turn 0 ----------

    [Fact]
    public async Task D1_ValidAtTurnZero_WritesTheFiles_TheManifest_AndAPlanValidateWorkAccepts()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(new(BaScenario.Valid())));

        var (exit, job) = await RunAsync(rig);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(0);
        rig.Models().Should().Equal("fable");
        File.ReadAllText(Path.Combine(Stories(rig), "s1-first", "story.yaml")).Should().Be(
            "# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)\nid: s1-first\ntitle: \"First part\"\ndependsOn: []\n");
        File.ReadAllText(Path.Combine(Stories(rig), "s1-first", "tasks", "t2-b", "task.yaml")).Should().Contain("dependsOn:\n  - t1-a\n").And.Contain("files:\n  - src/t2-b.cs\n").And.Contain("complexity: 3\n");
        Directory.GetDirectories(Path.Combine(rig.Repo, BaScenario.EpicFolder), ".coai-ba-staging-*").Should().BeEmpty("the staging folder is gone");

        var validated = CoaiMcp.ValidateWorkMode.Evaluate(_ => null, Path.Combine(rig.Repo, BaScenario.Plan), BaScenario.Plan);
        validated.Code.Should().Be(0, validated.Json);
        JsonDocument.Parse(validated.Json).RootElement.GetProperty("counts").GetProperty("tasks").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task TheManifest_HoldsEveryFile_WithTheHashOfItsLfText_AndTheResultSaysSo()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Stories(rig), "ba-manifest.json"))).RootElement;
        var files = manifest.GetProperty("files").EnumerateArray().ToList();
        files.Should().HaveCount(5, "two stories and three tasks");
        files.Select(file => file.GetProperty("kind").GetString()).Should().BeEquivalentTo("story", "story", "task", "task", "task");
        foreach (var file in files)
        {
            var path = file.GetProperty("path").GetString()!;
            path.Should().StartWith(BaScenario.EpicFolder + "/stories/");
            var text = File.ReadAllText(Path.Combine(rig.Repo, path.Replace('/', Path.DirectorySeparatorChar)));
            BaPublisher.Sha256Of(text.Replace("\r\n", "\n", StringComparison.Ordinal)).Should().Be(file.GetProperty("sha256").GetString(), path);
        }

        var result = JsonDocument.Parse(rig.World.Store.FolderOf(job.JobId)!.ReadResult()).RootElement;
        result.GetProperty("status").GetString().Should().Be("ok");
        (result.GetProperty("stories").GetInt32(), result.GetProperty("tasks").GetInt32(), result.GetProperty("repairTurns").GetInt32()).Should().Be((2, 3, 0));
        result.GetProperty("files").GetArrayLength().Should().Be(5);
        result.GetProperty("notes")[0].GetString().Should().Contain("second part");
    }

    // ---------- d1: valid after one repair, same session ----------

    [Fact]
    public async Task D1_ValidAfterOneRepair_InTheSameSession_OfTheSameModel()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(new(BaScenario.Wrong())), Says(new(BaScenario.Valid())));
        rig.Says("opus", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        job.Attempts.Should().ContainSingle("two turns are ONE attempt").Which.Model.Should().Be("fable");
        rig.Models().Should().Equal("fable", "fable");
        Resumed(rig.Launches()[0]).Should().BeEmpty("turn 0 is a fresh session");
        Resumed(rig.Launches()[1]).Should().Be("00000000-0000-0000-0000-000000000000", "the repair turn resumes the session the first turn reported");
        LastPrompt(rig, 1).Should().Contain("stories[0].id").And.Contain("cannot hold a path").And.Contain("complexity").And.Contain("parallel").And.Contain("WHOLE JSON object");
        rig.LedgerLines().Should().HaveCount(2, "every launch is one ledger line");
        Directory.Exists(Stories(rig)).Should().BeTrue();
    }

    [Fact]
    public async Task ARuleTheValidatorChecks_GoesBackToo_InTheBAsOwnIds()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(new(BaScenario.BrokenRule())), Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        LastPrompt(rig, 1).Should().Contain("[unknown-dependency] s1-first/t2-b:", "the error is told in the BA's own ids, not in a path it did not write");
    }

    [Fact]
    public async Task AnAnswerInACodeFence_IsAccepted_AndOneThatIsNotJson_IsRepaired()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(new("I think the plan should have two stories.")), Says(new("```json\n" + BaScenario.Valid().ToJsonString() + "\n```")));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        LastPrompt(rig, 1).Should().Contain("not ONE JSON object");
    }

    // ---------- d1: invalid three times ----------

    [Fact]
    public async Task D1_InvalidThreeTimes_EndsOutputInvalid_WithZeroFilesWritten_AndNoFailover()
    {
        await using var rig = await RigAsync();
        var before = BaScenario.Tree(rig.Repo);
        rig.Says("fable", Says(new(BaScenario.Wrong())));
        rig.Says("opus", Says(new(BaScenario.Valid())));

        var (exit, job) = await RunAsync(rig);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("output-invalid");
        job.Reason.Should().Contain("still wrong after 2 repair turns");
        job.RepairTurns.Should().Be(2);
        rig.Models().Should().Equal("fable", "fable", "fable");
        Directory.Exists(Stories(rig)).Should().BeFalse("zero files were written");
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before, "the repository is exactly as it was");
        var attempts = Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "attempts", "1");
        Directory.GetFiles(attempts).Select(Path.GetFileName).Should().Contain(["turn-0.txt", "turn-1.txt", "turn-2.txt", "evidence.txt"], "the output of every turn is kept in the job folder");
        File.ReadAllText(Path.Combine(attempts, "turn-2.txt")).Should().Contain("s1-../../../outside");
    }

    [Fact]
    public async Task AnIdThatClimbsOutOfItsFolder_IsNeverWritten_AnywhereOnDisk()
    {
        await using var rig = await RigAsync();
        var answer = BaScenario.Valid();
        answer["stories"]![0]!["id"] = "s1-../../../outside";
        answer["stories"]![1]!["tasks"]![0]!["id"] = "t1-a/b";
        rig.Says("fable", Says(new(answer)));
        var before = BaScenario.Tree(rig.Repo);

        var (_, job) = await RunAsync(rig, ["claude/fable"]);

        job.Stop.Should().Be("output-invalid");
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before);
        Directory.Exists(Path.Combine(rig.Repo, "outside")).Should().BeFalse();
        Directory.Exists(Path.Combine(Path.GetDirectoryName(rig.Repo)!, "outside")).Should().BeFalse("nothing was made beside the repository either");
        Directory.Exists(Stories(rig)).Should().BeFalse();
    }

    [Fact]
    public async Task ARunnerThatIsFencedBeforeItPublishes_WritesNothing_AndExits75()
    {
        await using var rig = await RigAsync();
        var before = BaScenario.Tree(rig.Repo);
        rig.Says("fable", Says(new(BaScenario.Valid())));
        var job = rig.NewJob(RoleDescriptors.Ba, RouteBoth, BaScenario.EpicFile);

        // A cancel (or a sweep) takes the job over while the files are staged: the generation moves, and the stale runner must not move them into place.
        var (exit, after) = await rig.RunAsync(job, [BaScenario.Descriptor(boundary: name =>
        {
            if (name == "manifest-written")
            {
                rig.World.Store.BumpGeneration(job.JobId).Should().BeGreaterThan(0);
            }
        })]);

        exit.Should().Be(RoleRunner.NotTheWriter);
        after.State.Should().NotBe(RoleJobStates.Succeeded);
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().BeEmpty("a stale runner publishes no result either");
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before, "nothing was moved into place and the staging folder is gone");
        Directory.GetDirectories(Path.Combine(rig.Repo, BaScenario.EpicFolder), ".coai-ba-staging-*").Should().BeEmpty();
    }

    // ---------- the declared refusal, failover ----------

    [Fact]
    public async Task ADeclaredRefusal_FailsOverToTheNextModel_WhichPlansTheEpic()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(new(FakeRoleAnswers.Refusal))).Says("opus", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.Model.Should().Be("claude/opus");
        job.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("fable", "refusal"), ("opus", string.Empty));
        Directory.Exists(Stories(rig)).Should().BeTrue();
    }

    [Fact]
    public async Task EveryModelRefusing_FailsTheJob_AndWritesNothing()
    {
        await using var rig = await RigAsync();
        rig.Says("*", Says(new(FakeRoleAnswers.Refusal)));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("models-exhausted");
        Directory.Exists(Stories(rig)).Should().BeFalse();
    }

    [Fact]
    public async Task ACrashOfTheFirstModel_ThenARepairOfTheSecond_ResumesTheSecondsOwnSession()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", FakeRoleAnswers.Crash()).Says("opus", Says(new(BaScenario.Wrong())), Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        rig.Models().Should().Equal("fable", "opus", "opus");
        Resumed(rig.Launches()[0]).Should().BeEmpty();
        Resumed(rig.Launches()[1]).Should().BeEmpty("a new model starts from turn 0");
        Resumed(rig.Launches()[2]).Should().NotBeEmpty("its repair turn continues ITS session");
        job.RepairTurns.Should().Be(1);
    }

    [Fact]
    public async Task AVendorThatNamesNoSession_CannotBeRepaired_SoTheAttemptEndsOutputInvalid_WithoutAnotherLaunch()
    {
        await using var rig = await RigAsync();
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(RoleStopFixtures.Load("claude-unknown-model-name-runs-on-the-default").StdOut)!.AsObject();
        envelope["result"] = BaScenario.Wrong().ToJsonString();
        envelope.Remove("session_id");
        rig.Says("fable", new { stdout = envelope.ToJsonString(), exit = 0 }).Says("opus", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.Stop.Should().Be("output-invalid");
        job.Reason.Should().Contain("named no session");
        rig.Models().Should().Equal("fable");
        job.RepairTurns.Should().Be(0);
        Directory.Exists(Stories(rig)).Should().BeFalse();
    }

    // ---------- d2: nothing outside the epic folder; an epic with a story is refused ----------

    [Fact]
    public async Task D2_NothingOutsideTheEpicFolderChanges_OnlyStoriesAreAdded()
    {
        await using var rig = await RigAsync();
        var before = BaScenario.Tree(rig.Repo);
        rig.Says("fable", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        var after = BaScenario.Tree(rig.Repo);
        after.Where(file => before.ContainsKey(file.Key)).Should().BeEquivalentTo(before, "every file that was there is byte for byte as it was");
        after.Keys.Except(before.Keys).Should().OnlyContain(path => path.StartsWith(BaScenario.EpicFolder + "/stories/", StringComparison.Ordinal))
            .And.HaveCount(6, "five files and the manifest, all under the epic's stories folder");
    }

    [Fact]
    public async Task D2_AnEpicThatHasAStory_IsRefusedBeforeALaunch()
    {
        await using var rig = await RigAsync();
        BaScenario.Write(rig.Repo, BaScenario.EpicFolder + "/stories/s1-already/story.yaml", "id: s1-already\ntitle: \"x\"\ndependsOn: []\n");
        rig.Says("fable", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("ba-epic");
        job.Reason.Should().Contain("already has 1 story").And.Contain("s1-already");
        rig.Launches().Should().BeEmpty("nothing was asked of a model");
    }

    [Fact]
    public async Task ADeviceLimit_IsInThePrompt_AndInTheCheck()
    {
        await using var rig = await RigAsync();
        rig.Setting("COAI_ROLES_WORK_METHOD", """{"maxTasksPerStory":1}""");
        var oneEach = BaScenario.Valid();
        oneEach["stories"] = new System.Text.Json.Nodes.JsonArray(
            BaScenario.Story("s1-first", "First", [], BaScenario.Task("t1-a")),
            BaScenario.Story("s2-second", "Second", ["s1-first"], BaScenario.Task("t1-b")));
        rig.Says("fable", Says(new(BaScenario.Valid())), Says(new(oneEach)));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        LastPrompt(rig, 0).Should().Contain("at most 1 tasks");
        LastPrompt(rig, 1).Should().Contain("[too-many-tasks] s1-first");
    }

    [Fact]
    public async Task AFaultElsewhereInTheFeature_IsNotTheBAsFault()
    {
        await using var rig = await RigAsync();
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/epic.yaml", "id: e2-more\ntitle: \"More\"\nmvp: false\ndependsOn:\n  - e1-core\nsecuirty: true\n");
        rig.Says("fable", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, "the unknown key of another epic was there before the BA ran: " + job.Reason);
        job.RepairTurns.Should().Be(0);
    }

    [Fact]
    public async Task ANextJob_RemovesTheStagingFolderAKilledRunLeft_ButNotALiveJobs()
    {
        await using var rig = await RigAsync();
        var epic = Path.Combine(rig.Repo, BaScenario.EpicFolder);
        var killed = Path.Combine(epic, ".coai-ba-staging-20260101T000000Z-aaaaaaaa");
        var live = rig.NewJob(RoleDescriptors.Ba, RouteBoth, BaScenario.EpicFile);
        var liveStaging = Path.Combine(epic, ".coai-ba-staging-" + live.JobId);
        Directory.CreateDirectory(Path.Combine(killed, "stories", "s1-half"));
        Directory.CreateDirectory(liveStaging);
        rig.Says("fable", Says(new(BaScenario.Valid())));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        Directory.Exists(killed).Should().BeFalse("a staging folder of a job that is not running is removed by the next job for the epic");
        Directory.Exists(liveStaging).Should().BeTrue("the job that made it is not over");
    }

    // ---------- start_role: the epic is looked at before a folder or a runner is made ----------

    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static RoleJobService ServiceFor(RoleJobWorld world) =>
        new(world.DataDir, name => name switch { "COAI_ROLE_LAUNCHER" => "setsid", "COAI_ROLE_CLAUDE_PERSON_SIGN_IN" => "1", _ => null }, world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted(), NoRunnerStarted.Succeeds),
            Descriptors = [BaScenario.Descriptor()],
            EnvironmentSource = new Hashtable(),
        };

    private static Task<string> Start(RoleJobService service, string repo, string? taskFile, string? brief = null) =>
        service.StartAsync(RoleDescriptors.Ba, repo, taskFile, brief, null, null, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task D2_AStartForAnEpicThatHasAStory_IsRefused_BeforeAnyFolderIsMade()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-ba-start-");
        BaScenario.Lay(repo);
        BaScenario.Write(repo, BaScenario.EpicFolder + "/stories/s1-already/story.yaml", "id: s1-already\n");

        var answer = JsonDocument.Parse(await Start(ServiceFor(world), repo, BaScenario.EpicFile)).RootElement;

        answer.GetProperty("error").GetString().Should().Contain("already has 1 story").And.Contain("never rewrites a plan");
        world.Store.JobIds().Should().BeEmpty();
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("no folder, no runner");
    }

    [Fact]
    public async Task TheShippedBaDescriptor_ItselfRefusesAnEpicWithAStory_AtTheStart()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-ba-start-");
        BaScenario.Lay(repo);
        BaScenario.Write(repo, BaScenario.EpicFolder + "/stories/s1-already/story.yaml", "id: s1-already\n");
        var service = new RoleJobService(world.DataDir, name => name switch { "COAI_ROLE_LAUNCHER" => "setsid", "COAI_ROLE_CLAUDE_PERSON_SIGN_IN" => "1", _ => null }, world.Log, _ => true)
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted(), NoRunnerStarted.Succeeds),
            EnvironmentSource = new Hashtable(),
        };

        var answer = JsonDocument.Parse(await Start(service, repo, BaScenario.EpicFile)).RootElement;

        answer.GetProperty("error").GetString().Should().Contain("already has 1 story");
        world.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task AStartForAnEpicWithNoStory_Succeeds_EvenWithAStagingFolderBesideIt_AndRoutesTheBaList()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-ba-start-");
        BaScenario.Lay(repo);
        Directory.CreateDirectory(Path.Combine(repo, BaScenario.EpicFolder, ".coai-ba-staging-leftover", "stories", "s1-x"));
        Directory.CreateDirectory(Path.Combine(repo, BaScenario.EpicFolder, "stories", "notes-not-a-story"));

        var answer = JsonDocument.Parse(await Start(ServiceFor(world), repo, BaScenario.EpicFile)).RootElement;

        answer.TryGetProperty("error", out _).Should().BeFalse(answer.ToString());
        answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/fable", "claude/opus");
    }

    [Theory]
    [InlineData("plan/epoch-1/e1-core/spec.yaml", "send taskFile")]
    [InlineData("", "send taskFile")]
    [InlineData("plan/epoch-1/e1-nothing/epic.yaml", "not an epic.yaml file inside the repository")]
    [InlineData("plan/epoch-1/epoch.yaml", "send taskFile")]
    public async Task AStartThatDoesNotNameAnEpicFile_IsRefused(string taskFile, string said)
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-ba-start-");
        BaScenario.Lay(repo);

        var answer = JsonDocument.Parse(await Start(ServiceFor(world), repo, taskFile.Length == 0 ? null : taskFile, taskFile.Length == 0 ? "plan the epic" : null)).RootElement;

        answer.GetProperty("error").GetString().Should().Contain(said);
        world.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task AnEpicWhoseIdIsNotItsFolder_OrOutsideAnEpochFolder_IsRefused()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-ba-start-");
        BaScenario.Lay(repo);
        BaScenario.Write(repo, "plan/epoch-1/e1-core/epic.yaml", "id: e9-other\ntitle: \"x\"\ndependsOn: []\n");
        BaScenario.Write(repo, "loose/e1-core/epic.yaml", "id: e1-core\ntitle: \"x\"\ndependsOn: []\n");

        var service = ServiceFor(world);
        JsonDocument.Parse(await Start(service, repo, BaScenario.EpicFile)).RootElement.GetProperty("error").GetString().Should().Contain("must be the name of its folder");
        JsonDocument.Parse(await Start(service, repo, "loose/e1-core/epic.yaml")).RootElement.GetProperty("error").GetString().Should().Contain("not in an epoch-N folder");
    }
}

/// <summary>A BA answer for FakeCli: JSON, or any text a model might say instead.</summary>
internal readonly record struct JsonNodeLike(string Text)
{
    public JsonNodeLike(System.Text.Json.Nodes.JsonObject answer)
        : this(answer.ToJsonString())
    {
    }
}
