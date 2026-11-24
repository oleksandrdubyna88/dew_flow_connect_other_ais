using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Scenario 14, the part a Linux box CAN run: a Windows host judged by the pure planner and finder (the shims are <c>.cmd</c>, a path with a <c>cmd.exe</c> operator is refused,
/// <c>C:\Users\A B\…</c> survives a read of JSON and TOML, and a Windows-spelled path in a WSL file is updated). The same on a real Windows host is NOT verified here.
/// </summary>
public sealed class AWindowsPathSurvivesTests
{
    private const string WindowsBinary = @"C:\Users\A B\AppData\Local\coai\coai-mcp.exe";

    private static InstallHost Windows(string path = @"C:\Users\A B\AppData\Roaming\npm;C:\Windows") => InstallHost.From(
        name => name switch { "USERPROFILE" => @"C:\Users\A B", "Path" => path, "LOCALAPPDATA" => @"C:\Users\A B\AppData\Local", _ => null },
        isWindows: true);

    private static PlanInputs Inputs(string binary, bool windows = true, bool exists = false) =>
        new(Wanted.Install, binary, BinaryExplicit: true, windows, _ => exists);

    [Fact]
    public void TheNpmShims_ArePickedByTheirCmdName()
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Users\A B\AppData\Roaming\npm\claude.cmd", @"C:\Users\A B\AppData\Roaming\npm\codex.cmd" };
        var finder = new ClientFinder(Windows(), present.Contains, new FakeLauncher());

        finder.PathOf(Client.Claude).Should().Be(@"C:\Users\A B\AppData\Roaming\npm\claude.cmd");
        finder.PathOf(Client.Codex).Should().Be(@"C:\Users\A B\AppData\Roaming\npm\codex.cmd");
        finder.PathOf(Client.Agy).Should().BeEmpty("a bare name is never guessed");
    }

    [Fact]
    public void APathWithASpace_IsRegisteredAsItIs_InTheClaudeEntryJson()
    {
        var plan = ClientPlan.Plan(new ClaudeMcp(), Windows(), ConfigRead.None, Inputs(WindowsBinary), interrupted: null);

        plan.Action.Should().Be(PlanAction.Add);
        plan.After!.Command.Should().Be(WindowsBinary);
        ClaudeMcp.ReadFile("{\"mcpServers\":{\"coai\":" + plan.After.RawJson + "}}").Should().BeOfType<ConfigRead.Found>()
            .Which.Entry!.Command.Should().Be(WindowsBinary, "the backslashes and the space survive a write and a read of the JSON");
    }

    [Fact]
    public void APathWithASpace_SurvivesTheTomlReader()
    {
        var read = CodexMcp.ReadFile("[mcp_servers.coai]\ncommand = \"C:\\\\Users\\\\A B\\\\AppData\\\\Local\\\\coai\\\\coai-mcp.exe\"\nargs = [\"--x\"]\n");

        read.Should().BeOfType<ConfigRead.Found>().Which.Entry!.Command.Should().Be(WindowsBinary);
        read.Should().BeOfType<ConfigRead.Found>().Which.Entry!.IsOurs.Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Users\a&b\coai-mcp.exe")]
    [InlineData(@"C:\Users\100%\coai-mcp.exe")]
    [InlineData(@"C:\Users\a^b\coai-mcp.exe")]
    [InlineData(@"C:\Users\a|b\coai-mcp.exe")]
    public void APathWithACmdOperator_IsRefused_OnWindows(string binary)
    {
        var plan = ClientPlan.Plan(new CodexMcp(), Windows(), ConfigRead.None, Inputs(binary), interrupted: null);

        plan.Action.Should().Be(PlanAction.Refuse);
        plan.Reason.Should().Contain("cmd.exe");
    }

    [Fact]
    public void TheSamePath_IsFineOnAPosixHost()
    {
        var plan = ClientPlan.Plan(new CodexMcp(), Windows(), ConfigRead.None, Inputs("/opt/a&b/coai-mcp", windows: false), interrupted: null);

        plan.Action.Should().Be(PlanAction.Add, "an ampersand is a letter in a Linux path");
    }

    [Fact]
    public void AWindowsSpelledPath_InAWslFile_IsUpdated_ItsEnvKept()
    {
        var env = new Dictionary<string, string> { ["COAI_DATA_DIR"] = "/data" };
        var registered = new McpEntry(@"C:\Users\A B\AppData\Local\coai\versions\1.0.0\coai-mcp.exe", ["--x"], env);
        var wsl = InstallHost.From(name => name == "HOME" ? "/home/p" : null, isWindows: false);

        var plan = ClientPlan.Plan(new CodexMcp(), wsl, new ConfigRead.Found(registered, []), Inputs("/home/p/coai/coai-mcp", windows: false, exists: false), interrupted: null);

        plan.Action.Should().Be(PlanAction.Update);
        plan.After!.Command.Should().Be("/home/p/coai/coai-mcp");
        plan.After.Env.Should().BeEquivalentTo(env);
        plan.After.Args.Should().Equal("--x");
    }
}
