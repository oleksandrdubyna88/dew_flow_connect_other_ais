using System.Text.Json.Nodes;
using CoaiMcp.Install;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The argument road of <c>claude mcp add-json</c> (a claude that cannot read stdin), after the plan review of 2026-11-24: it is refused when the resolved claude is a <c>.cmd</c>/<c>.bat</c> shim —
/// cmd.exe re-parses the JSON argument and can run shell syntax before the read-back — and the Windows quoting of what it does pass survives. The shim cannot be a file on this side (the finder
/// resolves <c>claude</c> on Linux), so the target is driven directly with a launcher that answers as a claude would; nothing here touches a real home.
/// </summary>
public sealed class AClaudeEntryNeverReachesACommandLineItCannotTrustTests
{
    private const string OldHelp = "Usage: claude mcp add-json [options] <name> <json>\n\nAdd an MCP server (stdio, SSE, HTTP, or WebSocket) with a JSON string\n";

    private const string NewHelp = "Usage: claude mcp add-json [options] <name> <json>\n\nAdd an MCP server (stdio, SSE, HTTP, or WebSocket) with a JSON string, or - to read it from stdin\n";

    /// <summary>A command that holds every character cmd.exe reads as syntax.</summary>
    private const string Metacharacters = """C:\Users\a&b %PATH% ^x\coai-mcp.exe""";

    private static string ShimRefusal(string path) =>
        $"claude at {path} is a .cmd/.bat shim; coai will not pass the entry on its command line; update Claude Code (claude update) so it reads stdin";

    [Theory]
    [InlineData("claude.cmd")]
    [InlineData("CLAUDE.BAT")]
    public async Task ACmdShimThatCannotReadStdin_IsRefusedBeforeAnyRemove_AndNothingIsPassedOnACommandLine(string name)
    {
        using var scene = new Scene(OldHelp, name);
        var before = Entry("/old/coai/versions/1.0.0/coai-mcp");
        scene.Put(before);

        var outcome = await scene.ApplyAsync(Entry(Metacharacters), before);

        outcome.Kind.Should().Be(OutcomeKind.Refused);
        outcome.Reason.Should().Be(ShimRefusal(scene.CliPath));
        scene.Requests.Should().ContainSingle("only the help was read").Which.Arguments.Should().Contain("--help");
        scene.Requests.SelectMany(request => request.Arguments).Should().NotContain(argument => argument.Contains("coai-mcp") || argument.Contains('&'));
        scene.FileText.Should().Contain("/old/coai/versions/1.0.0/coai-mcp", "the old entry is still there");
    }

    [Fact]
    public async Task ACmdShimThatReadsStdin_KeepsTheStdinRoad_WhateverTheEntryHolds()
    {
        using var scene = new Scene(NewHelp, "claude.cmd");

        var outcome = await scene.ApplyAsync(Entry(Metacharacters), null);

        outcome.Kind.Should().Be(OutcomeKind.Done);
        var add = scene.Requests.Single(request => request.Arguments.Contains("add-json") && !request.Arguments.Contains("--help"));
        add.Arguments.Last().Should().Be("-");
        add.StdIn.Should().Contain("coai-mcp.exe");
    }

    [Fact]
    public async Task AnExeThatCannotReadStdin_TakesTheJsonAsOneArgument_AWindowsPathWithItsBackslashesDoubled()
    {
        using var scene = new Scene(OldHelp, "claude.exe");
        const string Command = """C:\Users\Test User\AppData\Local\coai\versions\2.0.0\coai-mcp.exe""";

        var outcome = await scene.ApplyAsync(Entry(Command), null);

        outcome.Kind.Should().Be(OutcomeKind.Done, outcome.Reason);
        var add = scene.Requests.Single(request => request.Arguments.Contains("add-json") && !request.Arguments.Contains("--help"));
        add.Arguments.Take(5).Should().Equal("mcp", "add-json", "--scope", "user", "coai");
        add.Arguments.Should().HaveCount(6, "the JSON is ONE argument, whatever it holds");
        add.StdIn.Should().BeEmpty();
        add.Arguments[5].Should().Contain(Command.Replace("\\", "\\\\", StringComparison.Ordinal), "a backslash is doubled in JSON text and nothing else touches it");
        JsonNode.Parse(add.Arguments[5])!["command"]!.GetValue<string>().Should().Be(Command);
    }

    [Fact]
    public async Task AnExeThatCannotReadStdin_TakesAPathWithQuotesAndASpaceAsOneArgument()
    {
        using var scene = new Scene(OldHelp, "claude.exe");
        const string Command = """C:\Program Files\say "hi" \"there\"\coai-mcp.exe""";

        var outcome = await scene.ApplyAsync(Entry(Command), null);

        outcome.Kind.Should().Be(OutcomeKind.Done, outcome.Reason);
        var add = scene.Requests.Single(request => request.Arguments.Contains("add-json") && !request.Arguments.Contains("--help"));
        add.Arguments.Should().HaveCount(6);
        JsonNode.Parse(add.Arguments[5])!["command"]!.GetValue<string>().Should().Be(Command);
    }

    private static McpEntry Entry(string command) => new ClaudeMcp().NewEntry(command);

    /// <summary>One apply of the claude MCP target in a scratch home, with a launcher that answers the help as a claude would and writes the entry when it is added.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly TempDir _root = TempDir.For("coai-claude-road-");
        private readonly string _help;
        private readonly InstallHost _host;

        internal Scene(string help, string cliName)
        {
            _help = help;
            var home = _root.At("home");
            Directory.CreateDirectory(home);
            _host = InstallHost.From(name => name is "HOME" or "USERPROFILE" ? home : null, isWindows: false);
            CliPath = $"""C:\Users\Test User\AppData\Local\Programs\{cliName}""";
        }

        internal string CliPath { get; }

        internal List<ProcessRequest> Requests { get; } = [];

        internal string FileText => File.Exists(_host.ClaudeJson) ? File.ReadAllText(_host.ClaudeJson) : string.Empty;

        internal void Put(McpEntry entry) => File.WriteAllText(_host.ClaudeJson, $$"""{ "mcpServers": { "coai": {{entry.RawJson}} } }""");

        internal Task<TargetOutcome> ApplyAsync(McpEntry after, McpEntry? before)
        {
            var launcher = new Claude(this);
            var backup = InstallBackup.Create(_host, new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), 4242, "/x/coai-mcp", [new InstallBackup.Subject(Client.Claude, "mcp", _host.ClaudeJson, before, after)]);
            var context = new ApplyContext(_host, CliPath, new VendorCommands(_host, launcher, TimeSpan.FromSeconds(30), []), backup, InstallFaults.None, "2.1.310 (Claude Code)");
            var plan = new PlannedTarget(Client.Claude, "mcp", _host.ClaudeJson, before is null ? PlanAction.Add : PlanAction.Update, "claude mcp add-json", before, after, [], "test");

            return new ClaudeMcp().ApplyAsync(plan, context, CancellationToken.None);
        }

        public void Dispose() => _root.Dispose();

        /// <summary>Answers <c>--help</c> with the chosen text, and an add by writing the entry it was given (argument or stdin) the way a claude that accepted it would.</summary>
        private sealed class Claude(Scene scene) : IProcessLauncher
        {
            public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
            {
                scene.Requests.Add(request);
                if (request.Arguments.Contains("--help"))
                {
                    return Task.FromResult(new ProcessResult(0, scene._help, string.Empty, false));
                }

                if (request.Arguments.Contains("add-json"))
                {
                    var json = request.Arguments[^1] == "-" ? request.StdIn : request.Arguments[^1];
                    var root = File.Exists(scene._host.ClaudeJson) ? JsonNode.Parse(File.ReadAllText(scene._host.ClaudeJson))!.AsObject() : new JsonObject();
                    root["mcpServers"] = new JsonObject { ["coai"] = JsonNode.Parse(json) };
                    File.WriteAllText(scene._host.ClaudeJson, root.ToJsonString());

                    return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
                }

                if (request.Arguments.Contains("remove"))
                {
                    File.WriteAllText(scene._host.ClaudeJson, "{}");
                }

                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
            }
        }
    }
}
