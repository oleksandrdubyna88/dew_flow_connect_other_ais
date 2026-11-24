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
        Directory.CreateDirectory(At("job-1", "sub"));
        Put("aaa", "inside.txt");
        Put(Path.Combine("home", ".agents"), "hooks.json");
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

    [Fact]
    public async Task S7_TheServerBinary_ReadsARootThatSortsBeforeTheCoaiFolder() =>
        (await Answer("read_file", At("aaa", "inside.txt"), cwd: At("aaa"), entries: [At("aaa"), At("home")])).Decision.Should().Be("allow");

    [Fact]
    public async Task S8_TheServerBinary_DeniesTheCoaiFolder_WhereverThePayloadsCwdIs()
    {
        (await Answer("read_file", At("aaa", "inside.txt"), cwd: At("aaa"), entries: [At("aaa"), At("home")])).Decision.Should().Be("allow", "the control: the same payload shape, a file inside a root");
        (await Answer("read_file", At("home", ".agents", "hooks.json"), cwd: At("aaa"), entries: [At("aaa"), At("home")])).Should().Be(("deny", PathReason));
    }

    [Fact]
    public async Task S10_TheServerBinary_DeniesAPathOnANetworkPlace()
    {
        var remote = Path.Combine(OperatingSystem.IsWindows() ? @"\\coai-no-such-host\share" : "/net/coai-no-such-host/share", "x.txt");

        (await Answer("read_file", remote)).Should().Be(("deny", PathReason), "this only keeps the routing honest: a child process cannot show that no look was made");
    }

    [Fact]
    public async Task S11_TheServerBinary_DeniesADotDotWhereTheOsFoldsText_AndAllowsItElsewhere() =>
        (await Answer("read_file", Path.Combine(At("job-1"), "sub", "..", "inside.txt"))).Should()
            .Be(OperatingSystem.IsWindows() ? ("deny", PathReason) : ("allow", string.Empty), "it shows the real flag reaches the rule");

    [Fact]
    public async Task S9_TheServerBinary_ThatDoesNotRunFromAnAgentsFolder_DeniesAndSaysWhy()
    {
        var run = await Run("read_file", At("job-1", "inside.txt"), oldPayload: false, cwd: null, entries: null, workingFolder: At("home"));

        run.Said.Should().Be(("deny", PathReason));
        run.StdErr.Should().Contain("coai --agy-hook: denied, this hook does not run from its own .agents folder");
    }

    /// <summary>The payload as agy sends it: cwd = the first of the sorted workspacePaths, the Path the model chose.</summary>
    private string Payload(string tool, string? path, bool oldPayload, string? cwd = null, string[]? entries = null) => oldPayload
        ? $$"""{"toolCall":{"name":"{{tool}}","args":{} } }"""
        : new JsonObject
        {
            ["cwd"] = cwd ?? At("home"),
            ["hookEventName"] = "PreToolUse",
            ["toolCall"] = new JsonObject { ["args"] = new JsonObject { ["Path"] = path }, ["id"] = "call-1", ["name"] = tool },
            ["workspacePaths"] = new JsonArray([.. (entries ?? [At("home"), At("job-1")]).Select(entry => (JsonNode?)JsonValue.Create(entry))]),
        }.ToJsonString();

    /// <summary>Starts the server binary as agy's hook does, for one tool call, and returns its <c>decision</c> and <c>reason</c>.</summary>
    /// <remarks>
    /// Through the shared <see cref="ProcessLauncher"/>: both streams drained concurrently, a bounded wait, and the
    /// whole tree killed when the bound is exceeded — a server that ignored the argument and started the web host
    /// would otherwise serve for ever and hang the suite.
    /// </remarks>
    private async Task<(string Decision, string Reason)> Answer(string tool, string? path = null, bool oldPayload = false, string? cwd = null, string[]? entries = null, string? workingFolder = null) =>
        (await Run(tool, path, oldPayload, cwd, entries, workingFolder)).Said;

    private async Task<((string Decision, string Reason) Said, string StdErr)> Run(string tool, string? path, bool oldPayload, string? cwd, string[]? entries, string? workingFolder)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Exists(exe).Should().BeTrue($"the test needs the server binary beside it, at {exe}");

        var answer = await new ProcessLauncher().RunAsync(
            // The literal, not AntigravityReadOnly.HookArgument: the hook files already written on a host name it, so a
            // rename that moved the constant and the routing together must still fail here.
            new ProcessRequest(exe, ["--agy-hook"], workingFolder ?? At("home", ".agents")) { StdIn = Payload(tool, path, oldPayload, cwd, entries), Timeout = TimeSpan.FromSeconds(60) },
            TestContext.Current.CancellationToken);

        answer.TimedOut.Should().BeFalse($"the hook answers in milliseconds and must not start the web host. It said: {answer.StdOut}{answer.StdErr}");
        answer.ExitCode.Should().Be(0, $"the hook always exits 0 and says the decision in JSON. It said: {answer.StdOut}{answer.StdErr}");
        var said = JsonDocument.Parse(answer.StdOut).RootElement;

        return ((said.GetProperty("decision").GetString()!, said.TryGetProperty("reason", out var reason) ? reason.GetString()! : string.Empty), answer.StdErr);
    }
}
