using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Install;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// 2.0 release blocker (the owner's Windows hand check, 2026-11-24): claude 2.1.310's <c>mcp add-json</c> has no stdin ("&lt;name&gt; &lt;json&gt;" and nothing else; 2.1.331 adds "or - to read it from stdin"),
/// so the one road the installer knew — the JSON on stdin — rolled the target back four times with no reason anywhere. The installer now READS the capability (<c>claude mcp add-json --help</c>, bounded,
/// starts no server) before it writes: stdin when the CLI says so; the JSON as the argument when it does not and the entry holds no environment; a refusal, with a reason, when it does not and the entry
/// holds a value — because no value ever goes on argv. And every target that does not end done carries its reason, in the manifest and in the report.
/// </summary>
public sealed class AnOldClaudeReadsAddJsonFromItsArgumentTests
{
    private const string Sentinel = "sentinel-secret-7f3a9c";

    private const string OldVersion = "2.1.310";

    private static string StdinRefusal(string version) => $"claude {version} cannot read `mcp add-json` from stdin; update Claude Code (claude update)";

    private static JsonObject ManifestTarget(InstallWorld world, string cli = "claude", string kind = "mcp") =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(world.BackupFolders.Single(), "manifest.json")))!["targets"]!.AsArray()
            .Select(node => node!.AsObject()).Single(one => one["cli"]!.GetValue<string>() == cli && one["kind"]!.GetValue<string>() == kind);

    private static IEnumerable<string> AddJsonCalls(InstallWorld world) =>
        world.Calls.Where(line => line.StartsWith("claude mcp add-json ", StringComparison.Ordinal) && !line.Contains(" --help | ", StringComparison.Ordinal));

    [Fact]
    public async Task AnOldHelp_AndNoEnvironment_WritesTheEntryThroughTheArgument()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Result("claude").Should().Be("changed");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be(world.Binary);
        world.Calls.Should().Contain(line => line.StartsWith("claude mcp add-json --help | ", StringComparison.Ordinal), "the capability is READ before anything is written");
        var add = AddJsonCalls(world).Single();
        add.Should().Contain("add-json --scope user coai {").And.NotContain("coai - |", "the old CLI has no stdin form");
        world.Calls.Should().NotContain(line => line.StartsWith("claude mcp get") || line.StartsWith("claude mcp list"));
        ManifestTarget(world)["state"]!.GetValue<string>().Should().Be("verified");
    }

    [Fact]
    public async Task AnOldHelp_AndAnEntryWithoutEnvironment_IsUpdatedThroughTheArgument_WithItsArgumentsKept()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);
        world.Put(world.ClaudeJson, """{ "numStartups": 3, "mcpServers": { "coai": { "type": "stdio", "command": "/old/coai/versions/1.0.0/coai-mcp", "args": ["--x"] } } }""");

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var entry = InstallOutput.ClaudeEntry(world.ClaudeJsonText)!;
        entry["command"]!.GetValue<string>().Should().Be(world.Binary);
        entry["args"]!.AsArray().Select(one => one!.GetValue<string>()).Should().Equal("--x");
        world.Calls.Should().ContainSingle(line => line.StartsWith("claude mcp remove --scope user coai", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOldHelp_AndAnEnvironmentValue_IsRefusedWithTheReason_NothingIsRun_AndNoValueIsOnArgv()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);
        var before = $$"""
            { "numStartups": 3, "mcpServers": { "coai": { "type": "stdio", "command": "/old/coai/versions/1.0.0/coai-mcp", "args": ["--x"], "env": { "COAI_DATA_DIR": "/data/coai", "COAI_API_TOKEN": "{{Sentinel}}" } } } }
            """;
        world.Put(world.ClaudeJson, before);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78, err);
        output.Result("claude").Should().Be("refused");
        output.Target("claude", "mcp").GetProperty("reason").GetString().Should().Be(StdinRefusal(OldVersion));
        output.Client("claude").GetProperty("reason").GetString().Should().Contain(StdinRefusal(OldVersion));
        world.ClaudeJsonText.Should().Be(before, "a refused target leaves the file as it was");
        world.Calls.Should().NotContain(line => line.StartsWith("claude mcp remove", StringComparison.Ordinal), "the old entry is not removed when the new one cannot be added");
        AddJsonCalls(world).Should().BeEmpty();
        world.Calls.Should().NotContain(line => line.Contains(Sentinel) || line.Contains("/data/coai"), "no environment value is ever in argv");
        output.ToString().Should().NotContain(Sentinel);
        err.Should().NotContain(Sentinel);
        var line = ManifestTarget(world);
        line["state"]!.GetValue<string>().Should().Be("refused");
        line["reason"]!.GetValue<string>().Should().Be(StdinRefusal(OldVersion));
    }

    [Fact]
    public async Task AHelpThatCannotBeRead_IsNoStdin_AnEnvironmentValueIsRefused_AndAnEntryWithoutOneIsWrittenThroughTheArgument()
    {
        using var withValue = InstallWorld.Create(codex: false);
        withValue.Steer(Client.Claude, fail: "help", version: "2.1.331");
        InstallOutput.WithOldEntries(withValue);

        var (code, output, _) = await withValue.RunAsync("--binary", withValue.Binary);

        code.Should().Be(78);
        output.Target("claude", "mcp").GetProperty("reason").GetString().Should().Be(StdinRefusal("2.1.331"));
        AddJsonCalls(withValue).Should().BeEmpty();

        using var plain = InstallWorld.Create(codex: false);
        plain.Steer(Client.Claude, fail: "help");

        var (plainCode, plainOutput, err) = await plain.RunAsync("--binary", plain.Binary);

        plainCode.Should().Be(0, err);
        plainOutput.Result("claude").Should().Be("changed");
        AddJsonCalls(plain).Single().Should().NotContain("coai - |");
    }

    [Fact]
    public async Task AnUnknownVersion_IsLeftOutOfTheRefusalSentence()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, fail: "help,version");
        InstallOutput.WithOldEntries(world);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Target("claude", "mcp").GetProperty("reason").GetString().Should().Be("claude cannot read `mcp add-json` from stdin; update Claude Code (claude update)");
    }

    [Fact]
    public async Task ANewHelp_KeepsTheStdinRoad_EvenWithAnEnvironment()
    {
        using var world = InstallWorld.Create(codex: false);
        InstallOutput.WithOldEntries(world);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var add = AddJsonCalls(world).Single();
        add.Should().Contain("add-json --scope user coai - |", "stdin supported: today's road, unchanged").And.NotContain("/data/coai");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["env"]!["COAI_DATA_DIR"]!.GetValue<string>().Should().Be("/data/coai");
    }

    [Fact]
    public async Task ADryRun_ReadsNoHelp_AndRunsNothing()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);

        var (code, _, _) = await world.RunAsync("--binary", world.Binary, "--dry-run");

        code.Should().Be(0);
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailingAdd_LeavesItsReasonInTheManifestAndTheReport_WithNoValueInIt()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, fail: "add-json", echo: true);
        world.Put(world.ClaudeJson, $$"""
            { "mcpServers": { "coai": { "type": "stdio", "command": "/old/coai/versions/1.0.0/coai-mcp", "args": ["--x"], "env": { "COAI_API_TOKEN": "{{Sentinel}}" } } } }
            """);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(74, err);
        var reported = output.Target("claude", "mcp").GetProperty("reason").GetString()!;
        reported.Should().Contain("`claude mcp add-json` exited 1").And.Contain("fake claude: add-json failed").And.NotContain(Sentinel);
        var line = ManifestTarget(world);
        line["state"]!.GetValue<string>().Should().BeOneOf("rolledBack", "failed");
        line["reason"]!.GetValue<string>().Should().Contain("`claude mcp add-json` exited 1").And.Contain("fake claude: add-json failed").And.NotContain(Sentinel);
        err.Should().NotContain(Sentinel).And.Contain("fake claude: add-json failed", "the same reason reaches the terminal of a person who ran the mode by hand");
    }

    [Fact]
    public async Task AClientsReason_NamesTheTargetThatDidNotEndDone_NotTheOnesThatDid()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);
        InstallOutput.WithOldEntries(world);

        var (_, output, _) = await world.RunAsync("--binary", world.Binary);

        output.Target("claude", "hook").GetProperty("action").GetString().Should().NotBe("none", "the hook is still done");
        output.Client("claude").GetProperty("reason").GetString().Should().Be(StdinRefusal(OldVersion), "the sentence a person is shown holds the refusal and nothing about the targets that worked");
    }

    [Fact]
    public async Task AWindowsShapedPathAndQuotes_SurviveTheArgument_AsOneArgumentTheClientReadsBack()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the stand-in's .cmd wrapper re-parses a quote; the argument list itself is asserted below on every platform that can run the shell stand-in");
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);
        const string WindowsPath = """C:\Users\Test User\AppData\Local\coai\data""";
        const string Quoted = "say \"hi\" \\\"there\\\"";
        world.Put(world.ClaudeJson, new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["coai"] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "/old/coai/versions/1.0.0/coai-mcp",
                    ["args"] = new JsonArray("--x", WindowsPath, Quoted),
                },
            },
        }.ToJsonString());
        var launcher = new RecordingLauncher(new ProcessLauncher());

        var (code, _, err) = await world.RunAsync(world.Deps() with { Launcher = launcher }, "setup-clients", "--binary", world.Binary);

        code.Should().Be(0, err);
        var add = launcher.Requests.Single(request => request.Arguments.Contains("add-json") && !request.Arguments.Contains("--help"));
        add.Arguments.Take(5).Should().Equal("mcp", "add-json", "--scope", "user", "coai");
        add.Arguments.Should().HaveCount(6, "the JSON is ONE argument, whatever it holds");
        add.StdIn.Should().BeEmpty();
        var sent = JsonNode.Parse(add.Arguments[5])!;
        sent["command"]!.GetValue<string>().Should().Be(world.Binary);
        sent["args"]!.AsArray().Select(one => one!.GetValue<string>()).Should().Equal("--x", WindowsPath, Quoted);
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["args"]!.AsArray().Select(one => one!.GetValue<string>()).Should().Equal("--x", WindowsPath, Quoted);
    }

    [Fact]
    public async Task AWindowsShapedBinaryPath_IsOneArgument_WithItsBackslashesEscapedInTheJson()
    {
        // The entry's own command is replaced by the registered binary, so the Windows-shaped command comes in through an update of the args: the JSON text holds the path with doubled backslashes.
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, oldAddJson: true, version: OldVersion);
        const string WindowsPath = """C:\Users\Test User\AppData\Local\coai\versions\2.0.0\coai-mcp.exe""";
        world.Put(world.ClaudeJson, new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["coai"] = new JsonObject { ["type"] = "stdio", ["command"] = "/old/coai/versions/1.0.0/coai-mcp", ["args"] = new JsonArray(WindowsPath) },
            },
        }.ToJsonString());
        var launcher = new RecordingLauncher(new ProcessLauncher());

        var (code, _, err) = await world.RunAsync(world.Deps() with { Launcher = launcher }, "setup-clients", "--binary", world.Binary);

        code.Should().Be(0, err);
        var json = launcher.Requests.Single(request => request.Arguments.Contains("add-json") && !request.Arguments.Contains("--help")).Arguments[^1];
        json.Should().Contain(WindowsPath.Replace("\\", "\\\\", StringComparison.Ordinal), "a backslash is doubled in JSON text and nothing else touches it");
        JsonNode.Parse(json)!["args"]![0]!.GetValue<string>().Should().Be(WindowsPath);
    }

    /// <summary>Passes every launch on and keeps the request, so a test reads the argument LIST — which a log line that joins it with spaces cannot show.</summary>
    private sealed class RecordingLauncher(IProcessLauncher inner) : IProcessLauncher
    {
        internal List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return inner.RunAsync(request, ct);
        }
    }
}
