using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t3 — what a main AI reads about the scout and the judge: over real stdio the description of <c>start_role</c> names both, what each takes (<c>brief</c>, optional <c>taskFile</c>) and what each answers; the refusal of
/// an unknown role lists them; there is no new tool (d1). <c>role_status</c> returns the envelope exactly as <c>result.json</c> holds it (d2).
/// </summary>
/// <remarks>The wire is a real <c>coai-mcp</c> on stdio; the status check runs the real service, runner and tree check over FakeCli, so no model is called. In the serial <c>fakecli-env</c> collection.</remarks>
[Collection("fakecli-env")]
public sealed class ARoleToolTextNamesScoutAndJudgeTests
{
    private static readonly string[] ToolsOfThisEpoch = ["start_role", "role_status", "role_cancel"];

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<Dictionary<string, JsonElement>> ToolsAsync(System.Diagnostics.Process server)
    {
        using var list = await StdioServer.RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        return list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.Clone()).ToDictionary(tool => tool.GetProperty("name").GetString()!);
    }

    // ---------- d1: the tool list over real stdio ----------

    [Fact]
    public async Task D1_TheStartRoleDescription_NamesTheScoutAndTheJudge_WhatEachTakes_AndWhatEachAnswers()
    {
        using var data = TempDir.For("coai-scouttext-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);

            var tools = await ToolsAsync(server);

            var description = tools["start_role"].GetProperty("description").GetString()!;
            description.Should().Contain("`scout`").And.Contain("`judge`");
            description.Should().Contain("they need `brief`").And.Contain("may add `taskFile`").And.Contain("the file it is about");
            description.Should().Contain("READ-ONLY").And.Contain("`scout` models").And.Contain("`important` models only").And.Contain("a `model` outside that list is refused");
            description.Should().Contain("facts[{claim, evidence[{file, line, text, verified, why}]}]").And.Contain("files[{path, exists}]").And.Contain("unknown[]").And.Contain("counts, repairTurns, commit");
            description.Should().Contain("verdict: approve|revise|reject").And.Contain("findings[{id, severity").And.Contain("it writes no file");
            description.Should().Contain("coai checks every evidence item against the file itself");
            description.Should().Contain("`coder`").And.Contain("`ba`").And.Contain("`probe`", "the roles that were there stay named");
            var schema = tools["start_role"].GetProperty("inputSchema");
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().Contain(["role", "repoPath", "taskFile", "brief", "model", "timeoutMinutes", "workItem", "baseSha"]);
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().BeEquivalentTo(["role", "repoPath"], "brief and taskFile stay optional in the schema: which one a role needs is the role's, said in the description");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_ThereIsNoNewTool_TheScoutAndTheJudgeAreRoleCasesOfStartRole()
    {
        using var data = TempDir.For("coai-scouttext-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);

            var tools = await ToolsAsync(server);

            tools.Keys.Should().Contain(ToolsOfThisEpoch);
            tools.Keys.Should().NotContain(name => name.Contains("scout", StringComparison.OrdinalIgnoreCase) || name.Contains("judge", StringComparison.OrdinalIgnoreCase),
                "the scout and the judge are role cases of start_role, not tools of their own");
            tools.Keys.Where(name => name.EndsWith("_role", StringComparison.Ordinal) || name.StartsWith("role_", StringComparison.Ordinal)).Should().BeEquivalentTo(ToolsOfThisEpoch);
            tools["role_status"].GetProperty("description").GetString().Should().Contain("for the scout and the judge the envelope described under `start_role`");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_AnUnknownRolesRefusal_ListsTheScoutAndTheJudge_OverTheWire()
    {
        using var data = TempDir.For("coai-scouttext-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);

            var answer = await StdioServer.Call(server, 2, "start_role", $$"""{"role":"planner","repoPath":{{JsonSerializer.Serialize(data.Path)}},"brief":"x"}""");

            var error = answer.GetProperty("error").GetString()!;
            error.Should().Contain("no descriptor").And.Contain("probe, ba, coder, scout, judge", "the refusal names every role that can be started, in order");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_AScoutStartedOverTheWire_WithNoBrief_IsRefusedNamingTheBrief()
    {
        using var data = TempDir.For("coai-scouttext-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);

            var answer = await StdioServer.Call(server, 2, "start_role", $$"""{"role":"judge","repoPath":{{JsonSerializer.Serialize(data.Path)}}}""");

            answer.GetProperty("error").GetString().Should().Contain("the judge answers a question").And.Contain("brief");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    // ---------- d2: role_status returns the envelope result.json holds ----------

    [Theory]
    [InlineData("scout")]
    [InlineData("judge")]
    public async Task D2_RoleStatus_ReturnsTheEnvelopeJson_ThatResultJsonHolds(string role)
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        ReadOnlyScenario.Lay(rig.Repo);
        await rig.Git.CommitAsync("the files");
        var scout = role == "scout";
        rig.Says(scout ? "sonnet" : "fable", FakeRoleAnswers.ClaudeSays((scout ? ReadOnlyScenario.Scout() : JudgeScenario.Judge()).ToJsonString()));
        var job = rig.NewBriefJob(role, [scout ? "claude/sonnet" : "claude/fable"], ReadOnlyScenario.Brief);
        var (_, ended) = await rig.RunAsync(job, [scout ? ReadOnlyScenario.ScoutDescriptor() : JudgeScenario.JudgeDescriptor()]);
        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var service = new RoleJobService(rig.World.DataDir, _ => null, rig.World.Log, _ => true) { Clock = rig.World.Clock };

        var status = Parse(await service.StatusAsync(job.JobId, 0, TestContext.Current.CancellationToken));

        status.GetProperty("state").GetString().Should().Be("succeeded");
        var held = JsonNode.Parse(rig.World.Store.FolderOf(job.JobId)!.ReadResult())!;
        JsonNode.DeepEquals(JsonNode.Parse(status.GetProperty("result").GetRawText()), held).Should().BeTrue("role_status's result IS result.json, member for member");
        status.GetProperty("result").GetProperty("status").GetString().Should().Be("ok");
        status.GetProperty("result").GetProperty(scout ? "facts" : "findings").GetArrayLength().Should().BePositive();
    }
}
