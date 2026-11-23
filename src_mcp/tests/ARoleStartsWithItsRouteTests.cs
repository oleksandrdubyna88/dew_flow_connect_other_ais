using System.Collections;
using System.Text.Json;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s3 t3 — <c>start_role</c> answers the REAL <c>route[]</c> the router made, writes it (and the task's complexity and security flag) into <c>job.json</c>, and refuses — naming <c>COAI_ROLES_MODELS</c> — a role
/// that has no model before any folder is made. And the runner of a real binary writes its notices where the server writes them.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection: the real-binary test reads the environment the runner restores.</remarks>
[Collection("fakecli-env")]
public sealed class ARoleStartsWithItsRouteTests
{
    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static readonly RoleDescriptor Routed = new(FailoverProbeWork.Role, "a test-only role that asks its route's models", () => new FailoverProbeWork(TimeSpan.FromSeconds(1), []), RouteAs: "ba");

    private static RoleJobService ServiceFor(RoleJobWorld world, Dictionary<string, string?>? settings = null, IReadOnlyList<RoleDescriptor>? descriptors = null)
    {
        var values = settings ?? [];
        values.TryAdd("COAI_ROLE_LAUNCHER", "setsid");

        return new RoleJobService(world.DataDir, name => values.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted()),
            Descriptors = descriptors ?? [Routed, .. RoleDescriptors.Shipped],
        };
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string[] Route(JsonElement answer) => [.. answer.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()!)];

    private static Task<string> Start(RoleJobService service, string repo, string? model = null, string? taskFile = null, string? brief = "write the plan") =>
        service.StartAsync(FailoverProbeWork.Role, repo, taskFile, taskFile is null ? brief : null, model, null, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AStart_AnswersTheRoutesListInOrder_AndTheJobFileHoldsIt()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world);

        var answer = Parse(await Start(service, world.DataDir));

        Route(answer).Should().Equal("claude/fable", "claude/opus");
        world.Store.Read(answer.GetProperty("jobId").GetString()!)!.Route.Should().Equal("claude/fable", "claude/opus");
    }

    [Fact]
    public async Task AStart_WithAnExplicitModel_PutsItFirst_AndAnUnknownOneIsRefused()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world);

        Route(Parse(await Start(service, world.DataDir, model: "opus"))).Should().Equal("claude/opus", "claude/fable");
        var refused = Parse(await Start(service, world.DataDir, model: "mystery-9"));
        refused.GetProperty("error").GetString().Should().Contain("mystery-9").And.Contain("COAI_ROLES_MODELS");
        world.Store.JobIds().Should().HaveCount(1, "a refused start makes no folder");
    }

    [Fact]
    public async Task AStart_WhoseRoleHasNoModel_IsRefusedNamingTheSetting_BeforeAnyFolderIsMade()
    {
        using var world = new RoleJobWorld();
        // Codex as the main AI has no shipped lists: the roles' defaults there are empty until a person fills them.
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MAIN_AI"] = "codex" });

        var answer = Parse(await Start(service, world.DataDir));

        answer.GetProperty("error").GetString().Should().Contain("no model is set for role 'ba'").And.Contain("COAI_ROLES_MODELS");
        world.Store.JobIds().Should().BeEmpty();
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("no folder, no runner");
    }

    [Fact]
    public async Task AStart_ForAPersonsOwnList_RoutesItsVendorsInTheirOrder()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MODELS"] = """{"ba":[{"model":"opus","vendor":"claude"},{"model":"gpt-5.5","vendor":"codex"}]}""" });

        Route(Parse(await Start(service, world.DataDir))).Should().Equal("claude/opus", "codex/gpt-5.5");
    }

    [Theory]
    [InlineData("code", 3, "security: true\n", true, 3)]
    [InlineData("security", 3, "", false, 3)]
    [InlineData("code", 10, "", false, 10)]
    public async Task AStart_ForAnImportantTask_TakesTheImportantList_ReadFromTheTaskFile(string kind, int complexity, string extra, bool security, int stored)
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-route-repo-");
        File.WriteAllText(Path.Combine(repo, "task.yaml"), $"id: t1-demo\ntitle: \"Demo\"\nkind: {kind}\ncomplexity: {complexity}\n{extra}");

        var answer = Parse(await Start(ServiceFor(world), repo, taskFile: "task.yaml"));

        // The important list is fable, then codex's astra when the person has a codex row (the shipped default has).
        Route(answer).Should().StartWith("claude/fable").And.NotContain("claude/opus", "the role's own list is not used");
        var job = world.Store.Read(answer.GetProperty("jobId").GetString()!)!;
        (job.Security, job.Complexity).Should().Be((security, stored));
    }

    [Fact]
    public async Task AStart_ForAnImportantTask_RefusesAnExplicitModelOutsideTheImportantList()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-route-repo-");
        File.WriteAllText(Path.Combine(repo, "task.yaml"), "id: t1-demo\ntitle: \"Demo\"\nkind: security\ncomplexity: 3\n");

        var answer = Parse(await Start(ServiceFor(world), repo, model: "sonnet", taskFile: "task.yaml"));

        answer.GetProperty("error").GetString().Should().Contain("important").And.Contain("sonnet");
        world.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task AnEpicFileWithNoKindOrComplexity_RoutesByTheRolesListAlone()
    {
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-route-repo-");
        File.WriteAllText(Path.Combine(repo, "epic.yaml"), "id: e1-demo\ntitle: \"Demo\"\nmvp: true\ndependsOn: []\n");

        Route(Parse(await Start(ServiceFor(world), repo, taskFile: "epic.yaml"))).Should().Equal("claude/fable", "claude/opus");
    }

    [Fact]
    public async Task ATaskFileThatIsALinkOutOfTheRepository_IsNotRead_SoItsFlagsDecideNothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege there");
        using var world = new RoleJobWorld();
        using var repo = TempDir.For("coai-route-repo-");
        using var outside = TempDir.For("coai-route-outside-");
        File.WriteAllText(Path.Combine(outside, "secret.yaml"), "id: t1-demo\ntitle: \"Demo\"\nkind: security\ncomplexity: 3\n");
        File.CreateSymbolicLink(Path.Combine(repo, "link.yaml"), Path.Combine(outside, "secret.yaml"));

        var answer = Parse(await Start(ServiceFor(world), repo, taskFile: "link.yaml"));

        Route(answer).Should().Equal("claude/fable", "claude/opus");
        world.Store.Read(answer.GetProperty("jobId").GetString()!)!.Security.Should().BeFalse();
    }

    [Fact]
    public async Task TheProbe_AsksNoModel_SoItsRouteIsEmpty_AndItStillStarts()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MAIN_AI"] = "codex" });

        var answer = Parse(await service.StartAsync("probe", world.DataDir, null, "succeed", null, null, null, TestContext.Current.CancellationToken));

        answer.TryGetProperty("error", out _).Should().BeFalse();
        Route(answer).Should().BeEmpty();
    }

    [Fact]
    public async Task ARealRunner_WritesTheNoticeOfAFailedJobWhereTheServerWritesItsOwn_AndStatusSaysWhy()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        using var world = new RoleJobWorld();
        var values = new Dictionary<string, string?> { ["COAI_ROLE_LAUNCHER"] = "setsid" };
        var service = new RoleJobService(world.DataDir, name => values.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Runner = (ServerBinary.Path, []),
            EnvironmentSource = new Hashtable { ["PATH"] = Environment.GetEnvironmentVariable("PATH"), ["HOME"] = Environment.GetEnvironmentVariable("HOME"), ["COAI_DATA_DIR"] = world.DataDir },
        };

        var started = Parse(await service.StartAsync("probe", world.DataDir, null, "fail the model said no", null, null, null, TestContext.Current.CancellationToken));
        var id = started.GetProperty("jobId").GetString()!;
        var status = Parse(await service.StatusAsync(id, 30, TestContext.Current.CancellationToken));

        status.GetProperty("state").GetString().Should().Be("failed");
        var file = Path.Combine(world.DataDir, ServerNotices.Name);
        RoleJobWorld.WaitFor(() => File.Exists(file) && File.ReadAllText(file).Contains("role-job-failed", StringComparison.Ordinal), "the runner's notice of a failed job");
        File.ReadAllText(file).Should().Contain(id).And.Contain("the model said no");
    }
}
