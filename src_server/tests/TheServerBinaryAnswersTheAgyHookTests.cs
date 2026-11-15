using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Tests;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The Team server's own binary answers agy's <c>PreToolUse</c> hook, and allows exactly <c>view_file</c> and <c>read_file</c>.
/// </summary>
/// <remarks>
/// <para>agy 1.5.9 renamed its read tool from <c>view_file</c> to <c>read_file</c>. #995 made
/// <see cref="AntigravityReadOnly.Decide"/> allow both and deny every other tool. An agy reviewer started by the Team server
/// runs from the read-only folder whose hook starts THIS server binary with <see cref="AntigravityReadOnly.HookArgument"/>
/// (<c>Program.cs</c> routes it before the web host), and the server shares the library that holds <c>Decide</c>, so
/// the fix reaches a Team-server reviewer only in a server release that carries it. This test pins it where the server
/// answers it: a server that still denied <c>read_file</c> would leave every agy 1.5.9 review unable to read a file.</para>
/// <para>It runs the BUILT executable, as agy does — one argument, the payload on stdin — because the routing in
/// <c>Program.cs</c> and the decision are two places that must agree, and a call to <c>Decide</c> in-process sees only one.</para>
/// </remarks>
public sealed class TheServerBinaryAnswersTheAgyHookTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-server-agy-hook-");

    [Fact]
    public async Task TheServerBinary_AllowsViewFileAndReadFile_AndDeniesEveryOtherTool_AsAgyRunsTheHook()
    {
        var exe = Path.Combine(
            AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Exists(exe).Should().BeTrue($"the test needs the server binary beside it, at {exe}");

        var decisions = new Dictionary<string, string>();
        foreach (var tool in new[] { "read_file", "view_file", "write_file", "run_command" })
        {
            decisions[tool] = await DecisionAsync(exe, tool);
        }

        decisions.Should().Equal(
            new Dictionary<string, string>
            {
                ["read_file"] = "allow",
                ["view_file"] = "allow",
                ["write_file"] = "deny",
                ["run_command"] = "deny",
            },
            "agy 1.5.9 reads with read_file (#995); view_file stays allowed and every other tool is denied");
    }

    /// <summary>Starts the server binary as agy's hook does, for one tool call, and returns its <c>decision</c>.</summary>
    /// <remarks>
    /// Through the shared <see cref="ProcessLauncher"/>: both streams drained concurrently, a bounded wait, and the
    /// whole tree killed when the bound is exceeded — a server that ignored the argument and started the web host
    /// would otherwise serve for ever and hang the suite.
    /// </remarks>
    private async Task<string> DecisionAsync(string exe, string tool)
    {
        var payload = $$"""{"toolCall":{"name":"{{tool}}","args":{} } }""";

        var answer = await new ProcessLauncher().RunAsync(
            new ProcessRequest(exe, [AntigravityReadOnly.HookArgument], _dir) { StdIn = payload, Timeout = TimeSpan.FromSeconds(60) },
            TestContext.Current.CancellationToken);

        answer.TimedOut.Should().BeFalse($"the hook answers in milliseconds and must not start the web host. It said: {answer.StdOut}{answer.StdErr}");
        answer.ExitCode.Should().Be(0, $"the hook always exits 0 and says the decision in JSON. It said: {answer.StdOut}{answer.StdErr}");
        return JsonDocument.Parse(answer.StdOut).RootElement.GetProperty("decision").GetString()!;
    }

    public void Dispose() => _dir.Dispose();
}
