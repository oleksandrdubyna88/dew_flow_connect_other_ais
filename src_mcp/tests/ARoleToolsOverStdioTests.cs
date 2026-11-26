using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The three role tools over a REAL <c>coai-mcp</c> on real stdio (epoch 3 E1 s1): the tool list, the answer of <c>start_role</c> in two seconds or less, and scenario A1 end to end — the server that
/// started the job is KILLED (its whole process tree), the runner goes on, and a NEW server reads the job as <c>succeeded</c>.
/// </summary>
[Collection("fakecli-env")]
public sealed class ARoleToolsOverStdioTests
{
    private static (string, string)[] Environment(string launcher) => [("COAI_ROLE_LAUNCHER", launcher), ("COAI_LOG_LEVEL", "warning")];

    [Fact]
    public async Task TheThreeToolsAreListed_WithTheirArguments()
    {
        using var data = TempDir.For("coai-roletools-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);
            using var list = await StdioServer.RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var tools = list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(tool => tool.GetProperty("name").GetString()!);

            tools.Keys.Should().Contain(["start_role", "role_status", "role_cancel"]);
            var required = tools["start_role"].GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(name => name.GetString()).ToList();
            required.Should().BeEquivalentTo(["role", "repoPath"], "taskFile, brief, model, timeoutMinutes, workItem and baseSha are optional");
            tools["start_role"].GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo(["role", "repoPath", "taskFile", "brief", "model", "timeoutMinutes", "workItem", "baseSha"]);
            tools["role_status"].GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().Equal("jobId");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Theory]
    [InlineData("setsid")]
    [InlineData("auto")]
    public async Task A1_TheServerThatStartedAJobIsKilled_TheJobEndsSucceeded_AndANewServerReadsIt(string launcher)
    {
        if (launcher == "auto")
        {
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
        }
        else
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        }

        using var data = TempDir.For("coai-roletools-");
        string jobId;
        var first = StdioServer.Start(data.Path, alsoInTheEnvironment: Environment(launcher));
        try
        {
            await StdioServer.InitializeAsync(first);
            var clock = Stopwatch.StartNew();
            var started = await StdioServer.Call(first, 2, "start_role", $$"""{"role":"probe","repoPath":{{JsonSerializer.Serialize(data.Path)}},"brief":"sleep 4"}""");

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "start_role answers in two seconds or less, server start excluded");
            started.TryGetProperty("jobId", out var id).Should().BeTrue(started.ToString());
            jobId = id.GetString()!;
            started.GetProperty("launcher").GetString().Should().Be(launcher == "auto" ? "systemd-run" : SetsidTool.Road);
            var store = new RoleJobStore(data.Path, TimeProvider.System, Serilog.Core.Logger.None);
            RoleJobWorld.WaitFor(() => store.Read(jobId) is { State: RoleJobStates.Running }, "the runner to run");
        }
        finally
        {
            await StdioServer.StopAsync(first);
        }

        var second = StdioServer.Start(data.Path, alsoInTheEnvironment: Environment(launcher));
        try
        {
            await StdioServer.InitializeAsync(second);
            var status = await StdioServer.Call(second, 2, "role_status", $$"""{"jobId":"{{jobId}}","maxSeconds":30}""", timeoutSeconds: 60);

            status.GetProperty("state").GetString().Should().Be("succeeded", "the runner was detached from the server that started it, which is dead");
            status.GetProperty("result").GetProperty("probe").GetString().Should().Be("ok");
            status.GetProperty("generation").GetInt32().Should().Be(1);
        }
        finally
        {
            await StdioServer.StopAsync(second);
        }
    }

    [Fact]
    public async Task ARoleWithoutADescriptor_IsRefusedOverTheWire()
    {
        using var data = TempDir.For("coai-roletools-");
        var server = StdioServer.Start(data.Path);
        try
        {
            await StdioServer.InitializeAsync(server);

            var answer = await StdioServer.Call(server, 2, "start_role", $$"""{"role":"planner","repoPath":{{JsonSerializer.Serialize(data.Path)}},"brief":"x"}""");

            answer.GetProperty("error").GetString().Should().Contain("no descriptor");
            Directory.Exists(Path.Combine(data.Path, "roles", "jobs")).Should().BeFalse();
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }
}
