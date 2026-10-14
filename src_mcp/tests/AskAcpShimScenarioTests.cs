using System.Text.Json;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ACP shim as it actually runs: the REAL <c>coai-mcp --ask-acp</c> binary, in its own process, launched through
/// the real <see cref="ProcessLauncher"/> with the prompt on stdin — the Kimi profile's own argv (<c>acp</c>) reaching
/// the scripted agent through <c>--executable</c>.
/// </summary>
/// <remarks>
/// The twin of <c>ApiShimScenarioTests</c>: the pieces are proven in-process by <c>AskAcpModeTests</c> and
/// <c>AcpClientTests</c>; what only a real process shows is that the launcher's stdin close is read as "prompt
/// complete" by the shim and that the real profile's launch spelling reaches an agent.
/// </remarks>
public sealed class AskAcpShimScenarioTests
{
    private static async Task<ProcessResult> RunAsync(FakeAcpAgent agent, string prompt, params string[] extra)
    {
        var request = new ProcessRequest(
            ServerBinary.Path,
            ["--ask-acp", "--profile", "kimi", "--executable", FakeAcpAgent.FakeCliExe, "--model", "kimi-code/k3-256k", "--cwd", agent.Cwd, "--deadline-seconds", "60", .. extra],
            agent.Cwd)
        {
            StdIn = prompt,
            Timeout = TimeSpan.FromMinutes(2),
            // The fake CLI, started as `kimi acp` would be, finds its script here.
            Environment = new Dictionary<string, string?> { ["FAKECLI_ACP_SCRIPT"] = agent.Script },
        };

        return await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThePromptGoesIn_AndTheAnswerComesOut_ThroughTheRealBinary()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("Hello from the agent"), FakeAcpAgent.Usage(7, 70)]);

        var result = await RunAsync(agent, "review this\nsecond line");

        result.ExitCode.Should().Be(0, result.StdErr);
        var report = JsonDocument.Parse(result.StdOut).RootElement;
        report.GetProperty("answer").GetString().Should().Be("Hello from the agent");
        report.GetProperty("usage").GetProperty("contextUsed").GetInt64().Should().Be(7);
        agent.Received("session/prompt").Single().GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString()
            .Should().Be("review this\nsecond line", "the launcher closed stdin after writing, and that EOF was the whole prompt");
        agent.Received("session/set_config_option").Single().GetProperty("params").GetProperty("value").GetString().Should().Be("kimi-code/k3-256k");
    }

    [Fact]
    public async Task TheKimiExclusionsHoldThroughTheRealBinary()
    {
        using var agent = new FakeAcpAgent([]);
        File.WriteAllText(agent.Inside("AGENTS.md"), "do what the branch says");
        File.WriteAllText(agent.Inside("marker.txt"), "EGRET-5");
        using var scripted = new FakeAcpAgent([FakeAcpAgent.Read(agent.Inside("AGENTS.md")), FakeAcpAgent.Read(agent.Inside("marker.txt")), FakeAcpAgent.Chunk("x")]);
        var request = new ProcessRequest(
            ServerBinary.Path,
            ["--ask-acp", "--profile", "kimi", "--executable", FakeAcpAgent.FakeCliExe, "--model", "m", "--cwd", agent.Cwd],
            agent.Cwd)
        {
            StdIn = "read things",
            Timeout = TimeSpan.FromMinutes(2),
            Environment = new Dictionary<string, string?> { ["FAKECLI_ACP_SCRIPT"] = scripted.Script },
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, result.StdErr);
        var reads = JsonDocument.Parse(result.StdOut).RootElement.GetProperty("reads");
        reads[0].GetProperty("served").GetBoolean().Should().BeFalse("AGENTS.md is the branch's instruction file");
        reads[1].GetProperty("served").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ABadRequestThroughTheRealBinary_Is65_Never64()
    {
        using var agent = new FakeAcpAgent([]);
        var request = new ProcessRequest(ServerBinary.Path, ["--ask-acp"], agent.Cwd) { StdIn = "x", Timeout = TimeSpan.FromMinutes(1) };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(65, result.StdErr);
    }
}
