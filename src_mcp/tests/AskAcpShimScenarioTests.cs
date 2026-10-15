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
public sealed class AskAcpShimScenarioTests : IDisposable
{
    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    /// <summary>The Kimi shim runs only on a Windows host (D18): elsewhere it cannot key the sign-in lock and refuses, so a turn through the real binary is a Windows test.</summary>
    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Kimi runs on Windows hosts only (D18); the shim refuses elsewhere");

    /// <summary>
    /// The Kimi profile needs an owned home, the person's bin and the lock folder (E4.3): a fake person's, in the test's own folder. The agent, started
    /// as `kimi acp` would be but in an environment built from scratch, finds its script beside its working folder.
    /// </summary>
    private string[] KimiFlags(string model) =>
    [
        "--ask-acp", "--profile", "kimi", "--executable", FakeAcpAgent.FakeCliExe, "--model", model,
        "--home", _person.MadeHome().OwnedRoot, "--persons-bin", _person.Bin, "--locks", Path.Combine(_person.Folder, "locks"),
    ];

    private async Task<ProcessResult> RunAsync(FakeAcpAgent agent, string prompt, params string[] extra)
    {
        var request = new ProcessRequest(ServerBinary.Path, [.. KimiFlags("kimi-code/k3-256k"), "--cwd", agent.Cwd, "--deadline-seconds", "60", .. extra], agent.Cwd)
        {
            StdIn = prompt,
            Timeout = TimeSpan.FromMinutes(2),
        };

        return await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThePromptGoesIn_AndTheAnswerComesOut_ThroughTheRealBinary()
    {
        OnWindows();
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("Hello from the agent"), FakeAcpAgent.Usage(7, 70)], FakeAcpAgent.Models("kimi-code/k3-256k"));

        var result = await RunAsync(agent, "review this\nsecond line");

        result.ExitCode.Should().Be(0, result.StdErr);
        var report = JsonDocument.Parse(result.StdOut).RootElement;
        report.GetProperty("answer").GetString().Should().Be("Hello from the agent");
        report.GetProperty("usage").GetProperty("contextUsed").GetInt64().Should().Be(7);
        agent.Received("session/prompt").First().GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString()
            .Should().Be("review this\nsecond line", "the launcher closed stdin after writing, and that EOF was the whole prompt");
        agent.Received("session/set_config_option").Single().GetProperty("params").GetProperty("value").GetString().Should().Be("kimi-code/k3-256k");
    }

    [Fact]
    public async Task TheKimiExclusionsHoldThroughTheRealBinary()
    {
        OnWindows();
        using var scripted = new FakeAcpAgent([], FakeAcpAgent.Models("m"));
        File.WriteAllText(scripted.Inside("AGENTS.md"), "do what the branch says");
        File.WriteAllText(scripted.Inside("marker.txt"), "EGRET-5");
        File.WriteAllText(scripted.Script, $$"""{"log":{{FakeAcpAgent.Quote(scripted.Log)}},"steps":[{{FakeAcpAgent.Read(scripted.Inside("AGENTS.md"))}},{{FakeAcpAgent.Read(scripted.Inside("marker.txt"))}},{{FakeAcpAgent.Chunk("x")}}]{{FakeAcpAgent.Models("m")}}}""");
        var request = new ProcessRequest(ServerBinary.Path, [.. KimiFlags("m"), "--cwd", scripted.Cwd], scripted.Cwd)
        {
            StdIn = "read things",
            Timeout = TimeSpan.FromMinutes(2),
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
