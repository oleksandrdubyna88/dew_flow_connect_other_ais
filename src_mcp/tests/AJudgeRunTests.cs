using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t2 — the judge run (spec A, scenarios S8, S9, S12). The real runner, launcher, classifier, tree check and evidence check; FakeCli says the judge's answer. A judge job ends <c>succeeded</c> with a verdict, a summary,
/// findings whose evidence is checked as the scout's is, and a decision when the brief asked to choose (d1); each rule of S8 goes back as a repair turn, 2 at most, then <c>output-invalid</c> (d1); the route is the <c>important</c>
/// list and a named model outside it is refused before any folder, naming <c>COAI_ROLES_MODELS</c> (d2); and the judge writes no file (d3).
/// </summary>
[Collection("fakecli-env")]
public sealed class AJudgeRunTests
{
    private static readonly string[] Route = ["claude/fable", "codex/astra"];

    private static async Task<RoleFailoverRig> RigAsync()
    {
        var rig = await RoleFailoverRig.CreateAsync();
        ReadOnlyScenario.Lay(rig.Repo);
        await rig.Git.CommitAsync("the files");

        return rig;
    }

    private static object Says(JsonObject answer) => FakeRoleAnswers.ClaudeSays(answer.ToJsonString());

    private static Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleFailoverRig rig, string taskFile = "") =>
        rig.RunAsync(rig.NewBriefJob(RoleDescriptors.Judge, Route, "Is the router plan sound?", taskFile), [JudgeScenario.JudgeDescriptor()]);

    private static JsonElement Result(RoleFailoverRig rig, RoleJobRecord job) => JsonDocument.Parse(rig.World.Store.FolderOf(job.JobId)!.ReadResult()).RootElement.Clone();

    private static string LastPrompt(RoleFailoverRig rig, int launch) => rig.Launches()[launch][^1];

    private static JsonObject Finding(JsonObject answer, int index = 0) => answer["findings"]![index]!.AsObject();

    [Fact]
    public void TheJudgeRole_IsRegistered_RoutedAsImportant_AsksForABrief_AndHasItsOwnLookAtTheRequest()
    {
        var judge = RoleDescriptors.Find(RoleDescriptors.Shipped, "judge");

        judge.Should().NotBeNull();
        judge!.RouteAs.Should().Be("important", "the role is judge and its model list is important");
        judge.NeedsBrief.Should().BeTrue();
        judge.Check.Should().NotBeNull();
        RoleDescriptors.Shipped.Select(descriptor => descriptor.Role).Should().Equal("probe", "ba", "coder", "scout", "judge");
    }

    // ---------- d1: valid, the envelope ----------

    [Fact]
    public async Task D1_AValidJudge_Succeeds_WithVerdict_Summary_Findings_CheckedEvidence_AndTheDecision()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(JudgeScenario.Judge()));
        var before = BaScenario.Tree(rig.Repo);

        var (exit, job) = await RunAsync(rig, ReadOnlyScenario.Router);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        rig.Models().Should().Equal("fable");
        var argv = rig.Launches().Should().ContainSingle().Subject;
        argv.Should().Contain("--permission-mode").And.Contain("plan").And.NotContain("acceptEdits").And.NotContain("--restricted");
        argv[Array.IndexOf(argv, "--tools") + 1].Should().Be("Read,Glob,Grep", "the judge reads and searches: no shell, no write tool");
        LastPrompt(rig, 0).Should().Contain("Is the router plan sound?").And.Contain($"`{ReadOnlyScenario.Router}`").And.Contain("You write NO file");
        var result = Result(rig, job);
        result.GetProperty("status").GetString().Should().Be("ok");
        result.GetProperty("verdict").GetString().Should().Be("revise");
        result.GetProperty("summary").GetString().Should().Contain("one rule is missing");
        var findings = result.GetProperty("findings").EnumerateArray().ToList();
        findings.Select(finding => (finding.GetProperty("id").GetString(), finding.GetProperty("severity").GetString())).Should().Equal(("f1", "major"), ("f2", "nit"));
        findings[0].GetProperty("evidence")[0].GetProperty("verified").GetBoolean().Should().BeTrue();
        findings[0].GetProperty("evidence")[0].GetProperty("why").GetString().Should().Contain("holds the text");
        findings[0].GetProperty("fix").GetString().Should().Be("check the role");
        findings[1].GetProperty("evidence").GetArrayLength().Should().Be(0);
        result.GetProperty("decision").GetProperty("chosen").GetString().Should().Be("keep one router");
        result.GetProperty("decision").GetProperty("rejected")[0].GetProperty("option").GetString().Should().Be("two routers");
        var counts = result.GetProperty("counts");
        (counts.GetProperty("findings").GetInt32(), counts.GetProperty("evidence").GetInt32(), counts.GetProperty("verified").GetInt32()).Should().Be((2, 1, 1));
        result.GetProperty("repairTurns").GetInt32().Should().Be(0);
        result.GetProperty("commit").GetString().Should().HaveLength(40);
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before, "the judge writes no file");
    }

    [Fact]
    public async Task D1_WithNoDecisionAsked_TheEnvelopeHasNone_AndAnApproveWithNoFindingIsValid()
    {
        await using var rig = await RigAsync();
        var answer = JudgeScenario.Judge();
        answer["verdict"] = "approve";
        answer["findings"] = new JsonArray();
        answer.Remove("decision");
        rig.Says("fable", Says(answer));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        var result = Result(rig, job);
        result.TryGetProperty("decision", out _).Should().BeFalse("the decision is only when the brief asked to choose");
        result.GetProperty("findings").GetArrayLength().Should().Be(0);
        result.GetProperty("counts").GetProperty("findings").GetInt32().Should().Be(0);
    }

    // ---------- S8: the three rules go back as repair turns ----------

    [Fact]
    public async Task S8_AnApproveWithABlockingFinding_IsOneRepairTurn_InTheSameSession_ThenValid()
    {
        await using var rig = await RigAsync();
        var wrong = JudgeScenario.Judge();
        wrong["verdict"] = "approve";
        Finding(wrong)["severity"] = "blocking";
        rig.Says("fable", Says(wrong), Says(JudgeScenario.Judge()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1);
        job.Attempts.Should().ContainSingle().Which.Model.Should().Be("fable");
        rig.Models().Should().Equal("fable", "fable");
        rig.Launches()[1].SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault().Should().Be("00000000-0000-0000-0000-000000000000");
        LastPrompt(rig, 1).Should().Contain("\"approve\" cannot stand with a blocking finding").And.Contain("WHOLE JSON object");
    }

    [Fact]
    public async Task S8_AReviseWithNoFinding_AndAnIdUsedTwice_AreBothSentBackAtOnce()
    {
        await using var rig = await RigAsync();
        var noFinding = JudgeScenario.Judge();
        noFinding["findings"] = new JsonArray();
        var twice = JudgeScenario.Judge();
        Finding(twice, 1)["id"] = "f1";
        rig.Says("fable", Says(noFinding), Says(twice), Says(JudgeScenario.Judge()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(2);
        LastPrompt(rig, 1).Should().Contain("the verdict \"revise\" needs at least one finding");
        LastPrompt(rig, 2).Should().Contain("the id 'f1' is used more than once");
    }

    [Fact]
    public async Task S8_WrongThreeTimes_EndsOutputInvalid_WithNoResult_NoFailover_AndEveryAnswerKept()
    {
        await using var rig = await RigAsync();
        var wrong = JudgeScenario.Judge();
        wrong["verdict"] = "reject";
        wrong["findings"] = new JsonArray();
        rig.Says("fable", Says(wrong));
        rig.Says("gpt-6.2-astra", FakeRoleAnswers.CodexSays(JudgeScenario.Judge().ToJsonString()));

        var (exit, job) = await RunAsync(rig);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("output-invalid");
        job.Reason.Should().Contain("still wrong after 2 repair turns");
        job.RepairTurns.Should().Be(2);
        rig.Models().Should().Equal("fable", "fable", "fable");
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().BeEmpty();
        Directory.GetFiles(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "attempts", "1")).Select(Path.GetFileName).Should().Contain(["turn-0.txt", "turn-1.txt", "turn-2.txt"]);
    }

    [Fact]
    public async Task S8_AFindingsEvidence_IsCheckedAsTheScoutsIs_NotTrueGoesBack_AndStaysAsNotVerifiedAfterTheLastTurn()
    {
        await using var rig = await RigAsync();
        var untrue = JudgeScenario.Judge();
        Finding(untrue)["evidence"] = new JsonArray(ReadOnlyScenario.Item(ReadOnlyScenario.Router, 4, "this text is not on that line"), ReadOnlyScenario.Item(ReadOnlyScenario.Router, 2, ReadOnlyScenario.RouterLine2));
        rig.Says("fable", Says(untrue));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(2);
        LastPrompt(rig, 1).Should().Contain("findings[0].evidence[0]: not verified").And.Contain("does not hold the text");
        var evidence = Result(rig, job).GetProperty("findings")[0].GetProperty("evidence").EnumerateArray().ToList();
        evidence.Select(item => item.GetProperty("verified").GetBoolean()).Should().Equal(false, true);
        Result(rig, job).GetProperty("counts").GetProperty("verified").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \\t ")]
    [InlineData("\\u00A0")]
    public async Task S12_ABlankEvidenceText_IsAnErrorBeforeAnyLineIsRead(string escaped)
    {
        await using var rig = await RigAsync();
        var blank = JsonSerializer.Deserialize<string>($"\"{escaped}\"")!;
        var wrong = JudgeScenario.Judge();
        Finding(wrong)["evidence"] = new JsonArray(ReadOnlyScenario.Item(ReadOnlyScenario.Router, 99, blank));
        rig.Says("fable", Says(wrong), Says(JudgeScenario.Judge()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        LastPrompt(rig, 1).Should().Contain("findings[0].evidence[0].text:").And.Contain("never empty or only white space");
        Result(rig, job).GetProperty("counts").GetProperty("verified").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task S5_AFindingsPathThatLeavesTheRepository_IsAnError_AndIsRepaired()
    {
        await using var rig = await RigAsync();
        var wrong = JudgeScenario.Judge();
        Finding(wrong)["evidence"] = new JsonArray(ReadOnlyScenario.Item("../outside.cs", 1, "x"));
        rig.Says("fable", Says(wrong), Says(JudgeScenario.Judge()));

        var (_, job) = await RunAsync(rig);

        job.RepairTurns.Should().Be(1);
        LastPrompt(rig, 1).Should().Contain("findings[0].evidence[0].file:").And.Contain("leaves the repository");
    }

    [Fact]
    public async Task S6_ADeclaredRefusal_GoesToTheNextModel_OfTheImportantList()
    {
        await using var rig = await RigAsync();
        rig.Says("fable", Says(JsonNode.Parse(FakeRoleAnswers.Refusal)!.AsObject()));
        rig.Says("gpt-6.2-astra", FakeRoleAnswers.CodexSays(JudgeScenario.Judge().ToJsonString()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.Model.Should().Be("codex/astra");
        job.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("fable", "refusal"), ("astra", string.Empty));
    }

    // ---------- d3: the judge writes no file ----------

    [Fact]
    public async Task D3_TheTreeBeforeAndAfterAJudgeJob_IsTheSame_ThroughRepairTurnsToo()
    {
        await using var rig = await RigAsync();
        var before = BaScenario.Tree(rig.Repo);
        var wrong = JudgeScenario.Judge();
        wrong["verdict"] = "approve";
        Finding(wrong)["severity"] = "blocking";
        rig.Says("fable", Says(wrong), Says(JudgeScenario.Judge()));

        var (_, job) = await RunAsync(rig);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        BaScenario.Tree(rig.Repo).Should().BeEquivalentTo(before);
        Directory.GetDirectories(rig.Repo).Select(Path.GetFileName).Should().NotContain(name => name!.StartsWith(".coai-", StringComparison.Ordinal), "nothing of the job is in the repository");
    }

    // ---------- d2 / S9: the route ----------

    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static RoleJobService ServiceFor(RoleJobWorld world, Dictionary<string, string?>? settings = null)
    {
        var values = settings ?? [];
        values.TryAdd("COAI_ROLE_LAUNCHER", "setsid");

        return new RoleJobService(world.DataDir, name => values.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted(), NoRunnerStarted.Succeeds),
        };
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Task<string> Start(RoleJobService service, string repo, string? model = null, string? taskFile = null) =>
        service.StartAsync("judge", repo, taskFile, "Is the plan sound?", model, null, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task S9_TheRouteOfTheJudge_IsTheImportantList_AndTheJobsRoleIsJudge()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");

        var answer = Parse(await Start(ServiceFor(world), repo));

        answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/fable", "codex/astra");
        world.Store.ReadRequest(answer.GetProperty("jobId").GetString()!)!.Role.Should().Be("judge");
    }

    [Theory]
    [InlineData("fable", "claude/fable", "codex/astra")]
    [InlineData("astra", "codex/astra", "claude/fable")]
    public async Task S9_ANamedModelOnTheImportantList_GoesFirst(string named, string first, string second)
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");

        Parse(await Start(ServiceFor(world), repo, named)).GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal(first, second);
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("sonnet")]
    [InlineData("opus")]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("mystery-9")]
    public async Task S9_ANamedModelOutsideTheImportantList_IsRefusedBeforeAnyFolder_NamingTheSetting(string named)
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");

        var answer = Parse(await Start(ServiceFor(world), repo, named));

        answer.GetProperty("error").GetString().Should().Contain(named).And.Contain("COAI_ROLES_MODELS");
        world.Store.JobIds().Should().BeEmpty();
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("no folder, no runner");
    }

    [Fact]
    public async Task S9_TheRefusalOfAModelOutsideTheList_NamesTheListAndTheJudge()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");

        var error = Parse(await Start(ServiceFor(world), repo, "haiku")).GetProperty("error").GetString()!;

        error.Should().Contain("the judge runs only on the important list").And.Contain("fable, astra").And.Contain("add the model to the 'important' list");
    }

    [Fact]
    public async Task S9_APersonsOwnImportantList_IsTheJudgesRoute_AndItsModelsMayBeNamed()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MODELS"] = """{"important":[{"model":"opus","vendor":"claude"},{"model":"gpt-5.5","vendor":"codex"}]}""" });

        Parse(await Start(service, repo)).GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/opus", "codex/gpt-5.5");
        Parse(await Start(service, repo, "gpt-5.5")).GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("codex/gpt-5.5", "claude/opus");
        Parse(await Start(service, repo, "fable")).GetProperty("error").GetString().Should().Contain("COAI_ROLES_MODELS");
    }

    [Fact]
    public async Task S9_AnEmptyImportantList_IsRefusedNamingTheSetting_BeforeAnyFolder()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");
        // Codex as the main AI has no shipped lists: the important list is empty until a person fills it.
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MAIN_AI"] = "codex" });

        var error = Parse(await Start(service, repo)).GetProperty("error").GetString()!;

        error.Should().Contain("no model is set for role 'important'").And.Contain("COAI_ROLES_MODELS");
        world.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task S9_AJudgeForAnImportantTask_TakesTheSameList_AndTheScoutForOneTakesItToo()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");
        File.WriteAllText(repo.At("task.yaml"), "id: t1-demo\ntitle: \"Demo\"\nkind: security\ncomplexity: 3\n");
        var service = ServiceFor(world);

        var judge = Parse(await Start(service, repo, taskFile: "task.yaml"));
        var scout = Parse(await service.StartAsync("scout", repo, "task.yaml", "what is here?", null, null, null, TestContext.Current.CancellationToken));

        judge.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/fable", "codex/astra");
        // A scout for a task that is important takes the important list (router rule 1, unchanged).
        scout.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/fable", "codex/astra");
    }

    [Fact]
    public async Task S9_AJudgeForAnImportantTask_RefusesAModelOutsideTheListToo()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-judge-repo-");
        File.WriteAllText(repo.At("task.yaml"), "id: t1-demo\ntitle: \"Demo\"\nkind: security\ncomplexity: 3\n");

        var error = Parse(await Start(ServiceFor(world), repo, "haiku", "task.yaml")).GetProperty("error").GetString()!;

        error.Should().Contain("haiku").And.Contain("important");
        world.Store.JobIds().Should().BeEmpty();
    }
}
