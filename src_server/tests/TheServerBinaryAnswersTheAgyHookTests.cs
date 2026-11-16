using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Tests;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The Team server's own binary answers agy's <c>PreToolUse</c> hook as <c>AntigravityReadOnly.Decide</c> does: it allows
/// <c>view_file</c> and <c>read_file</c> on a file inside the folders the launch was given, and nothing else.
/// </summary>
/// <remarks>
/// <para>agy 1.5.9 renamed its read tool from <c>view_file</c> to <c>read_file</c> (#995), and the hook now also checks WHERE a reader
/// reads (research/PLAN_the_agy_hook_checks_the_path.md). An agy reviewer started by the Team server runs from the read-only
/// folder whose hook starts THIS server binary with the hook argument (<c>Program.cs</c> routes it before the web host), and the server
/// shares the library that holds <c>Decide</c>, so the guarantee reaches a Team-server reviewer only in a server release that carries it.
/// This test pins it where the server answers it: a server that still denied <c>read_file</c> would leave every agy 1.5.9 review unable to
/// read a file, and one that allowed every path would let a reviewer read what no one gave it.</para>
/// <para>It runs the BUILT executable, as agy does — one argument, the payload on stdin, the working directory the coai folder's
/// <c>.agents</c> — because the routing in <c>Program.cs</c> and the decision are two places that must agree, and a call to
/// <c>Decide</c> in-process sees only one.</para>
/// <para>The reasons are literals on purpose: the release step looks for these sentences in agy's output.</para>
/// </remarks>
public sealed class TheServerBinaryAnswersTheAgyHookTests : IDisposable
{
    private const string ToolNameReason = "coai: this consultation is read-only; only view_file or read_file runs";
    private const string PathReason = "coai: this consultation reads only inside the folders it was given";

    private readonly TempDir _dir = TempDir.For("coai-server-agy-hook-");

    public TheServerBinaryAnswersTheAgyHookTests()
    {
        Directory.CreateDirectory(At("home", ".agents"));
        Put("job-1", "inside.txt");
        Put("job-10", "secret.txt");
        Put("outside", "secret.txt");
    }

    public void Dispose() => _dir.Dispose();

    private string At(params string[] parts) => _dir.At(parts);

    private void Put(string folder, string name)
    {
        Directory.CreateDirectory(At(folder));
        File.WriteAllText(At(folder, name), "text");
    }

    [Fact]
    public async Task TheServerBinary_AnswersTheHookLikeDecide_AsAgyRunsIt()
    {
        var inside = At("job-1", "inside.txt");

        var answers = await Task.WhenAll(
            Answer("read_file", inside), Answer("view_file", inside),
            Answer("read_file", At("job-10", "secret.txt")),
            Answer("read_file", Path.Combine(At("job-1"), "..", "outside", "secret.txt")),
            Answer("write_file", inside), Answer("run_command", inside),
            Answer("read_file", path: null, oldPayload: true));

        answers.Should().Equal(
            [
                ("allow", string.Empty), ("allow", string.Empty),
                ("deny", PathReason), ("deny", PathReason),
                ("deny", ToolNameReason), ("deny", ToolNameReason),
                ("deny", PathReason),
            ],
            "agy 1.5.9 reads with read_file (#995); a read is allowed only inside a granted folder, and every other tool is denied");
    }

    [Fact]
    public async Task TheServerBinary_DeniesAReaderThroughALinkThatLeavesTheRoot()
    {
        try
        {
            File.CreateSymbolicLink(At("job-1", "leak"), At("outside", "secret.txt"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }

        (await Answer("read_file", At("job-1", "leak"))).Should().Be(("deny", PathReason));
    }

    /// <summary>The payload as agy sends it: cwd = the coai folder, workspacePaths = [cwd, root], the Path the model chose.</summary>
    private string Payload(string tool, string? path, bool oldPayload) => oldPayload
        ? $$"""{"toolCall":{"name":"{{tool}}","args":{} } }"""
        : new JsonObject
        {
            ["cwd"] = At("home"),
            ["hookEventName"] = "PreToolUse",
            ["toolCall"] = new JsonObject { ["args"] = new JsonObject { ["Path"] = path }, ["id"] = "call-1", ["name"] = tool },
            ["workspacePaths"] = new JsonArray(At("home"), At("job-1")),
        }.ToJsonString();

    /// <summary>Starts the server binary as agy's hook does, for one tool call, and returns its <c>decision</c> and <c>reason</c>.</summary>
    /// <remarks>
    /// Through the shared <see cref="ProcessLauncher"/>: both streams drained concurrently, a bounded wait, and the
    /// whole tree killed when the bound is exceeded — a server that ignored the argument and started the web host
    /// would otherwise serve for ever and hang the suite.
    /// </remarks>
    private async Task<(string Decision, string Reason)> Answer(string tool, string? path = null, bool oldPayload = false)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Exists(exe).Should().BeTrue($"the test needs the server binary beside it, at {exe}");

        var answer = await new ProcessLauncher().RunAsync(
            // The literal, not AntigravityReadOnly.HookArgument: the hook files already written on a host name it, so a
            // rename that moved the constant and the routing together must still fail here.
            new ProcessRequest(exe, ["--agy-hook"], At("home", ".agents")) { StdIn = Payload(tool, path, oldPayload), Timeout = TimeSpan.FromSeconds(60) },
            TestContext.Current.CancellationToken);

        answer.TimedOut.Should().BeFalse($"the hook answers in milliseconds and must not start the web host. It said: {answer.StdOut}{answer.StdErr}");
        answer.ExitCode.Should().Be(0, $"the hook always exits 0 and says the decision in JSON. It said: {answer.StdOut}{answer.StdErr}");
        var said = JsonDocument.Parse(answer.StdOut).RootElement;

        return (said.GetProperty("decision").GetString()!, said.TryGetProperty("reason", out var reason) ? reason.GetString()! : string.Empty);
    }
}
