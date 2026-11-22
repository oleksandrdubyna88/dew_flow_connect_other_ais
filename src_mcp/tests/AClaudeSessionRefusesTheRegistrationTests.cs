using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// v2 E3.S7 (the s6 leftover): inside a Claude Code session the real claude refuses <c>mcp add-json</c> on stdin. The installer meets that BEFORE the lock and the backup, says so in one sentence, exits 78 with
/// the other CLIs done — and does not get round the vendor's protection (no variable stripped, no other input form).
/// </summary>
public sealed class AClaudeSessionRefusesTheRegistrationTests
{
    private const string Sentence = "inside a Claude Code session the claude CLI refuses this registration; run the install from the extension or a normal terminal";

    [Fact]
    public async Task TheClaudeMcpTarget_IsRefused_Exit78_AndTheOthersAreDone()
    {
        using var world = InstallWorld.Create();
        world.ChildSession = true;

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78, err);
        output.Action("claude", "mcp").Should().Be("refuse");
        output.Target("claude", "mcp").GetProperty("reason").GetString().Should().Be(Sentence);
        output.Result("claude").Should().Be("refused");
        output.Client("claude").GetProperty("reason").GetString().Should().Contain(Sentence);
        output.Result("codex").Should().Be("changed", "the other CLIs are done");
        world.CodexTomlText.Should().Contain(world.Binary);
        world.ClaudeJsonText.Should().NotContain("coai-mcp");
        world.Calls.Should().NotContain(line => line.StartsWith("claude "), "the vendor was never asked — its protection is met, not tried");
    }

    [Fact]
    public async Task TheHook_NeedsNoVendorCommand_AndIsStillWritten()
    {
        using var world = InstallWorld.Create(codex: false);
        world.ChildSession = true;

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Action("claude", "hook").Should().Be("add");
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Should().HaveCount(1);
    }

    [Fact]
    public async Task WhenOnlyTheMcpEntryWouldChange_NoLockNoBackupNoStateFolderIsMade()
    {
        using var world = InstallWorld.Create(codex: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        File.WriteAllText(world.ClaudeJson, "{}");
        var backups = world.BackupFolders.Count;
        var calls = world.Calls.Count;
        var tree = world.HomeHash();
        world.ChildSession = true;

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Action("claude", "mcp").Should().Be("refuse");
        output.Action("claude", "hook").Should().Be("none");
        world.BackupFolders.Count.Should().Be(backups, "refused at plan time, before the lock and the backup");
        world.Calls.Count.Should().Be(calls);
        world.HomeHash().Should().Be(tree);
    }

    [Fact]
    public async Task ADryRun_AnswersTheCodeTheRealRunWould()
    {
        using var world = InstallWorld.Create(codex: false);
        world.ChildSession = true;

        var (code, output, _) = await world.RunAsync("--dry-run", "--binary", world.Binary);

        code.Should().Be(78);
        output.Action("claude", "mcp").Should().Be("refuse");
    }

    [Fact]
    public async Task AnEntryThatIsAlreadyRight_IsNone_NotRefused()
    {
        using var world = InstallWorld.Create(codex: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        world.ChildSession = true;

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, "nothing is registered, so nothing is refused");
        output.Action("claude", "mcp").Should().Be("none");
    }

    [Fact]
    public async Task ARemove_DoesNotReadStdin_SoItIsNotRefused()
    {
        using var world = InstallWorld.Create(codex: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        world.ChildSession = true;

        var (code, output, err) = await world.RunAsync("--remove");

        code.Should().Be(0, err);
        output.Action("claude", "mcp").Should().Be("remove");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().BeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("anything", true)]
    public void TheVariableTheVendorReads_IsTheOnlyOneThatTellsASession(string? value, bool inSession)
    {
        InstallHost.From(name => name == "CLAUDE_CODE_CHILD_SESSION" ? value : name == "HOME" ? "/home/p" : null, isWindows: false).InClaudeSession.Should().Be(inSession);
        InstallHost.From(name => name == "CLAUDECODE" ? "1" : name == "HOME" ? "/home/p" : null, isWindows: false).InClaudeSession
            .Should().BeFalse("CLAUDECODE is set in a shell a session starts, but the vendor's refusal does not read it — measured in claude 2.1.330");
    }

    [Fact]
    public void TheVendorsOwnEnvironment_IsNeverChanged_ToGetRoundTheRefusal()
    {
        var host = InstallHost.From(name => name switch { "HOME" => "/home/p", "CLAUDE_CODE_CHILD_SESSION" => "1", "CLAUDECODE" => "1", _ => null }, isWindows: false);

        host.VendorEnvironment.Keys.Should().BeEquivalentTo(["HOME", "USERPROFILE", "CLAUDE_CONFIG_DIR", "CODEX_HOME"], "only the four the host describes are set or removed; the session variables pass through to the vendor untouched");
    }
}
