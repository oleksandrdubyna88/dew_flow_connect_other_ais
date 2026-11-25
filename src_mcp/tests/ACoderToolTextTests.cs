using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s3 t4 — what a main AI reads about the coder: <c>start_role</c>'s description names it, an unknown role's refusal lists it, there is no <c>start_coder</c> tool (the coder is the role case, server.md § 19),
/// <c>baseSha</c> is an optional argument that reaches <c>request.json</c> and so the worktree, and <c>role_status</c> returns the envelope exactly as <c>result.json</c> holds it.
/// </summary>
/// <remarks>The wire is a real <c>coai-mcp</c> on stdio; the rest runs the real service, runner and worktree over FakeCli, so no model is called. In the serial <c>fakecli-env</c> collection.</remarks>
[Collection("fakecli-env")]
public sealed class ACoderToolTextTests
{
    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Error(JsonElement answer) => answer.GetProperty("error").GetString()!;

    private static RoleJobService ServiceFor(CoderRig rig) =>
        new(rig.World.DataDir, name => name == "COAI_ROLE_LAUNCHER" ? "setsid" : null, rig.World.Log, _ => true)
        {
            Clock = rig.World.Clock,
            Launcher = new DetachedLauncher(new NothingStarted()),
        };

    private static Task<string> Start(RoleJobService service, CoderRig rig, string? baseSha = null, string role = RoleDescriptors.Coder) =>
        service.StartAsync(role, rig.Repo, CoderRig.TaskFile, null, null, null, null, baseSha, TestContext.Current.CancellationToken);

    // ---------- d1: the tool list over real stdio ----------

    [Fact]
    public async Task D1_TheStartRoleDescription_NamesTheCoder_AndThereIsNoStartCoderTool()
    {
        using var data = TempDir.For("coai-codertext-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);
            using var list = await StdioServer.RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var tools = list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(tool => tool.GetProperty("name").GetString()!);

            var description = tools["start_role"].GetProperty("description").GetString()!;
            description.Should().Contain("`coder`").And.Contain("task.yaml").And.Contain("baseSha").And.Contain("coder/<jobId>");
            tools.Keys.Should().NotContain(name => name.Contains("coder", StringComparison.OrdinalIgnoreCase), "the coder is the role case of start_role, not a tool of its own");
            var schema = tools["start_role"].GetProperty("inputSchema");
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().Contain("baseSha");
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().BeEquivalentTo(["role", "repoPath"], "baseSha is optional like the other arguments");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_AnUnknownRolesRefusal_ListsTheCoder_OverTheWire()
    {
        using var data = TempDir.For("coai-codertext-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);

            var answer = await StdioServer.Call(server, 2, "start_role", $$"""{"role":"planner","repoPath":{{JsonSerializer.Serialize(data.Path)}},"brief":"x"}""");

            Error(answer).Should().Contain("no descriptor").And.Contain("probe, ba, coder, scout, judge", "the refusal names every role that can be started, the coder among them");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    // ---------- d2: baseSha, and the envelope in role_status ----------

    [Fact]
    public async Task D2_ABaseShaInTheStart_IsTheStartOfTheWorktree()
    {
        await using var rig = await CoderRig.CreateAsync();
        var first = rig.Base;
        await rig.Rig.Git.WriteAsync("later.txt", "a later commit\n");
        await rig.Rig.Git.CommitAsync("a later commit");
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());

        var started = Parse(await Start(ServiceFor(rig), rig, baseSha: first));

        started.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        var job = rig.World.Store.Read(started.GetProperty("jobId").GetString()!)!;
        rig.World.Store.ReadRequest(job.JobId)!.BaseSha.Should().Be(first, "the argument is written into request.json");
        var (_, ended) = await rig.RunAsync(job);
        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("baseSha").GetString().Should().Be(first).And.NotBe(await rig.Rig.Git.HeadAsync());
        File.Exists(Path.Combine(rig.WorktreeOf(job), "later.txt")).Should().BeFalse("the worktree started at the commit the start named");
    }

    [Fact]
    public async Task D2_WithoutABaseSha_TheBaseIsTheHeadOfTheRepositoryAtTheStart_NotAtTheRun()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var started = Parse(await Start(ServiceFor(rig), rig));
        var job = rig.World.Store.Read(started.GetProperty("jobId").GetString()!)!;
        await rig.Rig.Git.WriteAsync("after-the-start.txt", "committed after the start\n");
        await rig.Rig.Git.CommitAsync("after the start");

        rig.World.Store.ReadRequest(job.JobId)!.BaseSha.Should().Be(rig.Base, "the start read the HEAD and wrote it down");
        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("baseSha").GetString().Should().Be(rig.Base, "a commit made after the start is not the coder's base");
    }

    public static TheoryData<string, string> BadBases => new()
    {
        { "main", "40-character" },
        { "abc1234", "40-character" },
        { "--upload-pack=evil", "40-character" },
        { new string('a', 40), "not a commit" },
    };

    [Theory]
    [MemberData(nameof(BadBases))]
    public async Task D2_ABaseShaThatIsNotACommitOfTheRepository_IsRefused_BeforeAnyFolder(string baseSha, string says)
    {
        await using var rig = await CoderRig.CreateAsync(needsLayer: false);

        var answer = Parse(await Start(ServiceFor(rig), rig, baseSha));

        Error(answer).Should().Contain("baseSha").And.Contain(says);
        rig.World.Store.JobIds().Should().BeEmpty("a refused start makes no job");
    }

    [Fact]
    public async Task D2_ABaseSha_ForARoleThatHasNoBase_IsRefused_NotSilentlyDropped()
    {
        await using var rig = await CoderRig.CreateAsync(needsLayer: false);

        var answer = Parse(await Start(ServiceFor(rig), rig, rig.Base, role: RoleDescriptors.Probe));

        Error(answer).Should().Contain("baseSha").And.Contain("coder");
        rig.World.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task D2_RoleStatus_ReturnsTheEnvelopeJson_ThatResultJsonHolds()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs", "src/other.cs"), CoderRig.Report(notes: "done"));
        var started = Parse(await Start(ServiceFor(rig), rig));
        var job = rig.World.Store.Read(started.GetProperty("jobId").GetString()!)!;
        var (_, ended) = await rig.RunAsync(job);
        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);

        var status = Parse(await ServiceFor(rig).StatusAsync(job.JobId, 0, TestContext.Current.CancellationToken));

        status.GetProperty("state").GetString().Should().Be("succeeded");
        var held = JsonNode.Parse(rig.World.Store.FolderOf(job.JobId)!.ReadResult())!;
        JsonNode.DeepEquals(JsonNode.Parse(status.GetProperty("result").GetRawText()), held).Should().BeTrue("role_status's result IS result.json, member for member");
        status.GetProperty("result").GetProperty("branch").GetString().Should().Be($"coder/{job.JobId}");
        status.GetProperty("result").GetProperty("filesChanged").GetArrayLength().Should().Be(2);
    }
}
