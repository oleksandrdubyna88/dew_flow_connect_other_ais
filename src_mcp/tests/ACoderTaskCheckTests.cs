using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s3 t2 — the coder's request check: <c>taskFile</c> is a file called <c>task.yaml</c> inside the repository that passes the work-plan task rules, or the start is refused with a sentence before any folder is made (d1);
/// and the task's complexity and security go to the router: the band model then the coder list, the important list for a security or complexity-10 task, claude entries only, and a route with no claude model refused naming
/// <c>COAI_ROLES_MODELS</c> (d2).
/// </summary>
/// <remarks>
/// The tests hand <see cref="RoleJobService"/> their own descriptor for <c>coder</c> (the check, the route as <c>coder</c>, a work that is never run): registering the real one is story s3's task 3, which owns <c>CoderWork</c>.
/// Every refusal test has a positive twin on the same fixture, so a fixture the check quietly declines would be seen.
/// </remarks>
public sealed class ACoderTaskCheckTests
{
    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static readonly RoleDescriptor Coder = new("coder", "a test-only coder: the real check and route, a work that is never run", () => new FailoverProbeWork(TimeSpan.FromSeconds(1), []), RouteAs: "coder", Check: CoderTask.Refusal);

    private const string Story = "plan/epoch-1/e1-core/stories/s1-first";

    private static BaTask TaskOf(string id = "t1-a", int complexity = 4, string kind = "code", bool? security = null) =>
        new(id, "Write the thing", kind, complexity, [], false, ["src/thing.cs", "src/tests"], [new BaDod("d1", "the thing works"), new BaDod("d2", "the thing is tested")], security);

    /// <summary>A git repository with the demo plan and one story, committed (a coder's start reads the HEAD it will work from); the task is written by <see cref="Put"/>.</summary>
    private static async Task<TempDir> RepoAsync()
    {
        var repo = TempDir.For("coai-coder-task-");
        BaScenario.Lay(repo);
        BaScenario.Write(repo, $"{Story}/story.yaml", WorkPlanYaml.Story(new BaStory("s1-first", "A story", [], [])));
        foreach (var args in new string[][] { ["init", "-b", "main"], ["add", "-A"], ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", "the plan"] })
        {
            var ran = await new ProcessLauncher().RunAsync(new ProcessRequest("git", args, repo.Path));
            ran.ExitCode.Should().Be(0, ran.StdErr);
        }

        return repo;
    }

    /// <summary>Writes the task file below the story and answers its repository-relative path.</summary>
    private static string Put(string repo, BaTask task, Func<string, string>? change = null)
    {
        var relative = $"{Story}/tasks/{task.Id}/task.yaml";
        BaScenario.Write(repo, relative, (change ?? (text => text))(WorkPlanYaml.Task(task)));

        return relative;
    }

    private static RoleJobService ServiceFor(RoleJobWorld world, Dictionary<string, string?>? settings = null)
    {
        var values = settings ?? [];
        values.TryAdd("COAI_ROLE_LAUNCHER", "setsid");

        return new RoleJobService(world.DataDir, name => name == "COAI_ROLE_CLAUDE_PERSON_SIGN_IN" ? values.GetValueOrDefault(name) ?? "1" : values.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted()),
            Descriptors = [Coder, .. RoleDescriptors.Shipped],
        };
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Task<string> Start(RoleJobService service, string repo, string? taskFile, string? model = null, string? brief = null) =>
        service.StartAsync("coder", repo, taskFile, taskFile is null ? brief ?? "do something" : null, model, null, null, TestContext.Current.CancellationToken);

    private static string[] Route(JsonElement answer) => [.. answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()!)];

    private static string Error(JsonElement answer) => answer.GetProperty("error").GetString()!;

    private static void NoJobFolder(RoleJobWorld world)
    {
        world.Store.JobIds().Should().BeEmpty("a refused start makes no job");
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("a refused start makes no folder");
    }

    // ---------- d1: the check ----------

    [Fact]
    public async Task ATaskOfAValidPlan_Starts_WithItsRoute()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var file = Put(repo, TaskOf());

        var answer = Parse(await Start(ServiceFor(world), repo, file));

        answer.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        Route(answer).Should().Equal("claude/sonnet", "claude/opus", "codex/terra"); // codex is launchable since the flip commit of s5 t5; the default coder list names it
    }

    [Fact]
    public async Task AStart_WithNoTaskFile_IsRefused_AndAbriefIsNotATask()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        Put(repo, TaskOf());

        var answer = Parse(await Start(ServiceFor(world), repo, taskFile: null, brief: "fix the bug in the parser"));

        Error(answer).Should().Contain("taskFile").And.Contain("task.yaml").And.Contain("not a brief");
        NoJobFolder(world);
    }

    [Theory]
    [InlineData("plan/epoch-1/e1-core/epic.yaml")]
    [InlineData("plan/epoch-1/e1-core/stories/s1-first/tasks/t1-a/task.yml")]
    [InlineData("plan/epoch-1/e1-core/stories/s1-first/tasks/t1-a/subtask.yaml")]
    [InlineData("plan/epoch-1/e1-core/stories/s1-first/tasks/t1-a/task.yaml.bak")]
    [InlineData("plan/epoch-1/e1-core/stories/s1-first/tasks/t1-a")]
    public async Task ATaskFile_ThatIsNotCalledTaskYaml_IsRefused(string file)
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        Put(repo, TaskOf());

        var answer = Parse(await Start(ServiceFor(world), repo, file));

        Error(answer).Should().Contain("task.yaml");
        NoJobFolder(world);
    }

    [Fact]
    public async Task ATaskFile_ThatIsNotThere_AndOneOutsideTheRepository_AreRefused()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var file = Put(repo, TaskOf());

        Error(Parse(await Start(ServiceFor(world), repo, file.Replace("t1-a", "t9-gone", StringComparison.Ordinal)))).Should().Contain("task.yaml").And.Contain("inside the repository");
        Error(Parse(await Start(ServiceFor(world), repo, "../outside/task.yaml"))).Should().Contain("taskFile").And.Contain("..");
        Error(Parse(await Start(ServiceFor(world), repo, "/etc/task.yaml"))).Should().Contain("taskFile");
        NoJobFolder(world);
        Parse(await Start(ServiceFor(world), repo, file)).TryGetProperty("route", out _).Should().BeTrue("the same repository starts the real one");
    }

    [Fact]
    public async Task ATaskFile_ThatIsALinkOutOfTheRepository_IsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege there");
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        using var outside = TempDir.For("coai-coder-outside-");
        var file = Put(repo, TaskOf());
        File.Copy(Path.Combine(repo, file), Path.Combine(outside, "task.yaml"));
        File.Delete(Path.Combine(repo, file));
        File.CreateSymbolicLink(Path.Combine(repo, file), Path.Combine(outside, "task.yaml"));

        var answer = Parse(await Start(ServiceFor(world), repo, file));

        Error(answer).Should().Contain("inside the repository");
        NoJobFolder(world);
    }

    [Fact]
    public async Task ATaskYaml_ThatIsNotInAWorkPlan_IsRefused_NamingTheShape()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        BaScenario.Write(repo, "src/task.yaml", WorkPlanYaml.Task(TaskOf()));
        BaScenario.Write(repo, "task.yaml", WorkPlanYaml.Task(TaskOf()));
        var file = Put(repo, TaskOf());

        foreach (var loose in new[] { "src/task.yaml", "task.yaml" })
        {
            Error(Parse(await Start(ServiceFor(world), repo, loose))).Should().Contain("work plan").And.Contain("feature.yaml");
        }

        NoJobFolder(world);
        Parse(await Start(ServiceFor(world), repo, file)).TryGetProperty("route", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("an unknown key", "bogus: 1\n", "unknown-key")]
    [InlineData("a bad kind", "kind: magic\n", "bad-value")]
    public async Task ATask_ThatBreaksTheShapeRules_IsRefused_NamingTheRule(string why, string extra, string code)
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var file = Put(repo, TaskOf(), text => why == "a bad kind" ? text.Replace("kind: code", extra.TrimEnd(), StringComparison.Ordinal) : text + extra);

        var answer = Parse(await Start(ServiceFor(world), repo, file));

        Error(answer).Should().Contain("work-plan task rules").And.Contain($"[{code}]", why);
        NoJobFolder(world);
    }

    [Fact]
    public async Task ATask_WithNoDod_AFolderThatIsNotItsId_ADuplicateDodId_AndAContradictingSecurityFlag_AreEachRefused()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var noDod = Put(repo, TaskOf("t1-a") with { Dod = [] }, text => text.Replace("dod:", "dod: []", StringComparison.Ordinal));
        var twin = Put(repo, TaskOf("t2-b") with { Dod = [new BaDod("d1", "one"), new BaDod("d1", "two")] });
        var wrongId = Put(repo, TaskOf("t3-c"), text => text.Replace("id: t3-c", "id: t3-other", StringComparison.Ordinal));
        var contradiction = Put(repo, TaskOf("t4-d", kind: "security", security: false));
        var service = ServiceFor(world);

        Error(Parse(await Start(service, repo, noDod))).Should().Contain("[empty-dod]");
        Error(Parse(await Start(service, repo, twin))).Should().Contain("[duplicate-dod-id]");
        Error(Parse(await Start(service, repo, wrongId))).Should().Contain("[folder-id-mismatch]");
        Error(Parse(await Start(service, repo, contradiction))).Should().Contain("[security-contradiction]");
        NoJobFolder(world);
    }

    [Fact]
    public async Task ABrokenOtherTask_IsNotThisTasksFault()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var good = Put(repo, TaskOf("t1-a"));
        Put(repo, TaskOf("t2-b") with { Dod = [] });

        Parse(await Start(ServiceFor(world), repo, good)).TryGetProperty("route", out _).Should().BeTrue("only the errors of THIS task file count");
    }

    // ---------- d2: the route ----------

    private static Task<string> StartWith(RoleJobWorld world, string repo, BaTask task, Dictionary<string, string?>? settings = null, string? model = null) =>
        Start(ServiceFor(world, settings), repo, Put(repo, task), model);

    [Fact]
    public async Task AComplexityFourTask_GetsTheBandModel_ThenTheCoderList_InOrder()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var settings = new Dictionary<string, string?> { ["COAI_ROLES_MODELS"] = """{"coder":[{"model":"opus","vendor":"claude"},{"model":"haiku","vendor":"claude"}]}""" };

        var answer = Parse(await StartWith(world, repo, TaskOf(complexity: 4), settings));

        Route(answer).Should().Equal("claude/sonnet", "claude/opus", "claude/haiku");
        var job = world.Store.Read(answer.GetProperty("jobId").GetString()!)!;
        (job.Complexity, job.Security).Should().Be((4, false));
    }

    [Theory]
    [InlineData(10, null, "complexity 10")]
    [InlineData(3, true, "security: true")]
    public async Task AnImportantTask_GetsTheImportantList_NotTheCoderList(int complexity, bool? security, string reason)
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var settings = new Dictionary<string, string?> { ["COAI_ROLES_MODELS"] = """{"important":[{"model":"fable","vendor":"claude"},{"model":"opus","vendor":"claude"}],"coder":[{"model":"haiku","vendor":"claude"}]}""" };

        var answer = Parse(await StartWith(world, repo, TaskOf(complexity: complexity, security: security), settings));

        Route(answer).Should().Equal("claude/fable", "claude/opus");
        var job = world.Store.Read(answer.GetProperty("jobId").GetString()!)!;
        (job.Security, job.Complexity).Should().Be((security == true, complexity), reason);
    }

    [Fact]
    public async Task AVendorThatIsNotShipped_IsDroppedFromACoderRoute_AndOneThatShips_IsKept_InTheCoderListAndInTheImportantList()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var settings = new Dictionary<string, string?>
        {
            ["COAI_ROLES_MODELS"] = """{"coder":[{"model":"AGY","vendor":"agy"},{"model":"opus","vendor":"claude"},{"model":"x-1","vendor":"nonesuch"}],"important":[{"model":"fable","vendor":"claude"},{"model":"AGY","vendor":"agy"},{"model":"x-1","vendor":"nonesuch"}]}""".Replace("AGY", "gemini-4-argon-medium"),
        };

        Route(Parse(await StartWith(world, repo, TaskOf("t1-a", complexity: 4), settings))).Should().Equal("claude/sonnet", "agy/gemini-4-argon-medium", "claude/opus");
        Route(Parse(await StartWith(world, repo, TaskOf("t2-b", complexity: 10), settings))).Should().Equal("claude/fable", "agy/gemini-4-argon-medium");
    }

    [Fact]
    public async Task ARouteWithNoVendorThatShips_IsRefused_NamingTheSetting_AndMakesNoFolder()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();
        var settings = new Dictionary<string, string?>
        {
            ["COAI_ROLES_MODELS"] = """{"coder":[{"model":"x-1","vendor":"nonesuch"}]}""",
            ["COAI_ROLES_BANDS"] = """[{"min":0,"max":10,"model":"x-1","vendor":"nonesuch"}]""",
        };

        var answer = Parse(await StartWith(world, repo, TaskOf(complexity: 4), settings));

        Error(answer).Should().Contain("claude").And.Contain("COAI_ROLES_MODELS").And.Contain("nonesuch/x-1");
        NoJobFolder(world);
    }

    [Fact]
    public async Task AnExplicitAgyModel_NowGoesFirst_SinceAgyShips()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();

        Route(Parse(await StartWith(world, repo, TaskOf(complexity: 4), model: "gemini-4-argon-medium"))).Should().StartWith("agy/gemini-4-argon-medium");
    }

    [Fact]
    public async Task AnExplicitClaudeModel_StillGoesFirst()
    {
        using var world = new RoleJobWorld();
        using var repo = await RepoAsync();

        Route(Parse(await StartWith(world, repo, TaskOf(complexity: 4), model: "opus"))).Should().StartWith("claude/opus");
    }

    [Fact]
    public void Narrow_RefusesTheModelTheCallerNamed_WhenItsVendorIsNotLaunchable_AndDropsTheListEntriesThatAreNot()
    {
        var agyOff = CoderVendorSet.Of(AgyCoderBed.Table(launchable: false), AgyCoderBed.Vendor());
        var routed = new RouteResult.Routed([new ModelRef("gemini-4-argon-medium", "agy"), new ModelRef("sonnet", "claude")], "the model the caller named, then the coder list");

        CoderRoute.Narrow(routed, "gemini-4-argon-medium", vendors: agyOff).Should().BeOfType<RouteResult.Refused>().Which.Reason.Should().StartWith("the coder cannot run gemini-4-argon-medium on agy: ").And.Contain("COAI_ROLES_MODELS");
        CoderRoute.Narrow(routed, "sonnet", vendors: agyOff).Should().BeOfType<RouteResult.Routed>().Which.Models.Should().BeEquivalentTo(new[] { new ModelRef("sonnet", "claude") }, "a claude name asked for is not a refusal; the list entry of a vendor that is off is dropped");
        CoderRoute.Narrow(routed, vendors: agyOff).Should().BeOfType<RouteResult.Routed>().Which.Models.Should().BeEquivalentTo(new[] { new ModelRef("sonnet", "claude") }, "no model named by the caller: the entries of a vendor that is off are dropped");
    }

    [Fact]
    public void Narrow_KeepsTheOrderOfTheEntriesOfEveryVendorThatShips_AndSaysWhatItDropped()
    {
        var routed = new RouteResult.Routed([new ModelRef("gpt-5.5", "codex"), new ModelRef("opus", "claude"), new ModelRef("x-1", "nonesuch"), new ModelRef("gemini-3", "agy"), new ModelRef("sonnet", "claude")], "the band, then the coder list");

        var narrowed = CoderRoute.Narrow(routed).Should().BeOfType<RouteResult.Routed>().Subject;

        narrowed.Models.Should().Equal(new ModelRef("gpt-5.5", "codex"), new ModelRef("opus", "claude"), new ModelRef("gemini-3", "agy"), new ModelRef("sonnet", "claude"));
        narrowed.Why.Should().StartWith("the band, then the coder list").And.Contain("nonesuch/x-1").And.NotContain("codex/gpt-5.5", "codex ships since its flip").And.NotContain("agy/gemini-3", "agy ships since its flip");
        CoderRoute.Narrow(new RouteResult.Routed([new ModelRef("opus", "claude")], "x")).Should().BeOfType<RouteResult.Routed>().Which.Why.Should().Be("x", "nothing dropped, nothing said");
        CoderRoute.Narrow(new RouteResult.Refused("because")).Should().BeOfType<RouteResult.Refused>().Which.Reason.Should().Be("because");
    }

    [Fact]
    public async Task TheCheckHandsThePromptTheTaskFacts_ThatTheFileSays()
    {
        using var repo = await RepoAsync();
        var file = Put(repo, TaskOf("t1-a", complexity: 7, kind: "test", security: true));

        var (site, refusal) = CoderTask.Locate(repo, file);

        refusal.Should().BeEmpty();
        site.Should().NotBeNull();
        (site!.Id, site.Title, site.Kind, site.Complexity, site.Security, site.TaskRel).Should().Be(("t1-a", "Write the thing", "test", 7, true, file));
        site.Files.Should().Equal("src/thing.cs", "src/tests");
        site.Dod.Should().Equal(new WorkDod("d1", "the thing works"), new WorkDod("d2", "the thing is tested"));
    }
}
