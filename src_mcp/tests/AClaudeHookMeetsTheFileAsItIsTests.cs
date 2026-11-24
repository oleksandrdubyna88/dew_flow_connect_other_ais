using System.Text.Json.Nodes;
using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Scenarios 4, 8, 10, 16, 17 and 18 (hook halves), v2 E3.S7 t2: an old hook at another path is MOVED, not doubled; a malformed settings.json refuses the hook alone; a file that changes under the
/// installer is read again and, past three tries, refused with nothing written; a symbolic link stays a link; and the hook is restored and removed like the MCP entry.
/// </summary>
public sealed class AClaudeHookMeetsTheFileAsItIsTests
{
    private const string OldPath = "/old/coai/versions/1.0.0/coai-mcp";

    private static string WithOldHook(int timeout = 25) => $$"""
        {
          "model": "opus",
          "hooks": {
            "SessionStart": [
              { "hooks": [ { "type": "command", "command": "node", "args": ["/x/other.mjs"] } ] },
              { "hooks": [ { "type": "command", "command": "{{OldPath}}", "args": ["--roles", "--format", "claude-hook"], "timeout": {{timeout}} } ] }
            ]
          }
        }
        """;

    [Fact]
    public async Task Scenario4_AnOldHookAtAnotherPath_BecomesOneHandlerAtTheNewPath_ItsOtherKeysKept()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, WithOldHook());

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Action("claude", "hook").Should().Be("update");
        var handlers = ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText);
        handlers.Should().HaveCount(1, "one handler, not two");
        handlers[0]["command"]!.GetValue<string>().Should().Be(world.Binary);
        handlers[0]["timeout"]!.GetValue<int>().Should().Be(25, "the person's own timeout stays; only the command is replaced");
        world.ClaudeSettingsText.Should().NotContain(OldPath);
        ClaudeHookFixtures.AddedLines(WithOldHook().Replace(OldPath, InstallOutput.JsonPath(world.Binary)), world.ClaudeSettingsText).Should().BeEmpty("the one changed line is the command, in place");
        JsonNode.Parse(world.ClaudeSettingsText)!["hooks"]!["SessionStart"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("node", "the foreign group did not move");
    }

    [Fact]
    public async Task Scenario8_AMalformedSettings_RefusesTheHookAlone_TheMcpEntryIsDone()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, "{ \"hooks\": { oops");
        world.Put(world.ClaudeJson, InstallOutput.LivedInClaudeJson);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Action("claude", "mcp").Should().Be("add");
        output.Action("claude", "hook").Should().Be("refuse");
        output.Target("claude", "hook").GetProperty("reason").GetString().Should().Contain("settings.json").And.Contain("cannot be changed");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().NotBeNull("the MCP half was done");
        world.ClaudeSettingsText.Should().Be("{ \"hooks\": { oops", "its bytes are unchanged");
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("{ \"hooks\": [1] }")]
    [InlineData("{ \"hooks\": { \"SessionStart\": {} } }")]
    [InlineData("{ // a comment\n \"hooks\": {} }")]
    public async Task AWronglyShapedSettings_IsRefused_BytesUnchanged(string bad)
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, bad);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Action("claude", "hook").Should().Be("refuse");
        world.ClaudeSettingsText.Should().Be(bad);
    }

    private static InstallFaults ForeignWriterOnAttempts(int lastAttempt, List<int> seen) => new(
        InstallFaults.None.AfterState,
        (file, attempt) =>
        {
            seen.Add(attempt);
            if (attempt <= lastAttempt)
            {
                File.WriteAllText(file, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), "\"model\": \"[^\"]*\"", $"\"model\": \"changed-{attempt}\""));
            }
        });

    [Fact]
    public async Task Scenario10_AFileThatChangesBetweenReadAndWrite_IsReadAgain_AndTheOtherWritersChangeSurvives()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, ClaudeHookFixtures.Lived);
        var seen = new List<int>();

        var (code, output, err) = await world.RunAsync(world.Deps(faults: ForeignWriterOnAttempts(2, seen)), "setup-clients", "--binary", world.Binary);

        code.Should().Be(0, err);
        seen.Should().Equal(1, 2, 3);
        output.Action("claude", "hook").Should().Be("add");
        world.ClaudeSettingsText.Should().Contain("\"model\": \"changed-2\"", "the other writer's last change is not overwritten with the text read first");
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Should().HaveCount(1);
        Directory.GetFiles(Path.GetDirectoryName(world.ClaudeSettings)!).Should().ContainSingle(file => file.EndsWith("settings.json"), "no temporary file is left");
    }

    [Fact]
    public async Task Scenario10_AFileThatChangesEveryTime_IsRefusedAfterThreeTries_NothingWrittenByUs()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, ClaudeHookFixtures.Lived);
        var seen = new List<int>();

        var (code, output, _) = await world.RunAsync(world.Deps(faults: ForeignWriterOnAttempts(99, seen)), "setup-clients", "--binary", world.Binary);

        code.Should().Be(78);
        seen.Should().Equal(1, 2, 3);
        output.Result("claude").Should().Be("refused");
        output.Target("claude", "hook").GetProperty("reason").GetString().Should().Contain("changed between being read and being written").And.Contain("3 times");
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Should().BeEmpty();
        world.ClaudeSettingsText.Should().Be(ClaudeHookFixtures.Lived.Replace("\"model\": \"opus\"", "\"model\": \"changed-3\""), "the file is exactly what the other writer left");
        Directory.GetFiles(Path.GetDirectoryName(world.ClaudeSettings)!).Should().ContainSingle(file => file.EndsWith("settings.json"), "no temporary file is left");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().NotBeNull("the MCP half, a different file, was done");
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Scenario18_ASymlinkedSettings_StaysALink_AndItsTargetIsWritten()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symbolic links need a privilege on Windows");
        using var world = InstallWorld.Create(codex: false);
        var target = Path.Combine(world.Root, "dotfiles", "settings.json");
        world.Put(target, ClaudeHookFixtures.Lived);
        Directory.CreateDirectory(Path.GetDirectoryName(world.ClaudeSettings)!);
        File.CreateSymbolicLink(world.ClaudeSettings, target);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        new FileInfo(world.ClaudeSettings).LinkTarget.Should().Be(target, "the link is still there, pointing where it did");
        ClaudeHookFixtures.OurHandlers(File.ReadAllText(target)).Should().HaveCount(1);
        ClaudeHookFixtures.WithoutOurs(File.ReadAllText(target)).ToJsonString().Should().Be(JsonNode.Parse(ClaudeHookFixtures.Lived)!.ToJsonString());
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task TheFilesModeIsKept()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX modes");
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, ClaudeHookFixtures.Lived);
        File.SetUnixFileMode(world.ClaudeSettings, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        File.GetUnixFileMode(world.ClaudeSettings).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
    }

    [Fact]
    public async Task Scenario15_ADryRun_WritesNoSettingsFile()
    {
        using var world = InstallWorld.Create(codex: false);

        var (code, output, _) = await world.RunAsync("--dry-run", "--binary", world.Binary);

        code.Should().Be(0);
        output.Action("claude", "hook").Should().Be("add");
        File.Exists(world.ClaudeSettings).Should().BeFalse();
        world.BackupFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario17_Remove_TakesTheHookAway_AllElseEqual_AndASecondRemoveIsNone()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, ClaudeHookFixtures.Lived);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var (code, output, err) = await world.RunAsync("--remove");

        code.Should().Be(0, err);
        output.Action("claude", "hook").Should().Be("remove");
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Should().BeEmpty();
        world.ClaudeSettingsText.Should().Be(ClaudeHookFixtures.Lived, "the lines that were added are gone and nothing else moved");
        (await world.RunAsync("--remove")).Out.Action("claude", "hook").Should().Be("none");
    }

    [Fact]
    public async Task Scenario17_Remove_OfAHookWeCreatedTheFileFor_LeavesNoEmptyContainers()
    {
        using var world = InstallWorld.Create(codex: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        (await world.RunAsync("--remove")).Code.Should().Be(0);

        JsonNode.Parse(world.ClaudeSettingsText)!.AsObject().Should().BeEmpty("the file held only our group, so what is left is an empty object");
    }

    [Fact]
    public async Task Scenario17_Remove_KeepsAForeignHandlerInOurGroup()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, $$"""
            { "hooks": { "SessionStart": [ { "hooks": [
              { "type": "command", "command": "{{InstallOutput.JsonPath(world.Binary)}}", "args": ["--roles", "--format", "claude-hook"], "timeout": 10 },
              { "type": "command", "command": "node", "args": ["/x/keep.mjs"] }
            ] } ] } }
            """);

        var (code, _, err) = await world.RunAsync("--remove");

        code.Should().Be(0, err);
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Should().BeEmpty();
        JsonNode.Parse(world.ClaudeSettingsText)!["hooks"]!["SessionStart"]![0]!["hooks"]!.AsArray().Select(handler => handler!["command"]!.GetValue<string>()).Should().Equal("node");
    }

    [Fact]
    public async Task Scenario16_Restore_PutsTheOldHookBack_AndTakesAnAddedOneAway()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, WithOldHook());
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var (code, output, err) = await world.RunAsync("--restore", "latest");

        code.Should().Be(0, err);
        output.Action("claude", "hook").Should().Be("restore");
        world.ClaudeSettingsText.Should().Be(WithOldHook(), "the old command is back, in its own line");

        using var fresh = InstallWorld.Create(codex: false);
        fresh.Put(fresh.ClaudeSettings, ClaudeHookFixtures.Lived);
        (await fresh.RunAsync("--binary", fresh.Binary)).Code.Should().Be(0);
        (await fresh.RunAsync("--restore", "latest")).Code.Should().Be(0);
        fresh.ClaudeSettingsText.Should().Be(ClaudeHookFixtures.Lived);
    }

    [Fact]
    public async Task ARestoreIsRefused_WhenTheHookIsNoLongerWhatTheInstallWrote()
    {
        using var world = InstallWorld.Create(codex: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        var other = world.AnotherBinary("opt", "second");
        File.WriteAllText(world.ClaudeSettings, File.ReadAllText(world.ClaudeSettings).Replace(InstallOutput.JsonPath(world.Binary), InstallOutput.JsonPath(other)));

        var (code, output, _) = await world.RunAsync("--restore", "latest");

        code.Should().Be(78);
        output.Action("claude", "hook").Should().Be("refuse");
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Single()["command"]!.GetValue<string>().Should().Be(other);
    }

    [Fact]
    public async Task AnotherLiveCoaiMcpAlreadyInTheHook_IsLeftStanding_UnlessTheBinaryIsNamed()
    {
        using var world = InstallWorld.Create(codex: false);
        var first = world.AnotherBinary("opt", "first");
        (await world.RunAsync("--binary", first)).Code.Should().Be(0);

        var (code, output, _) = await world.RunAsync(world.Deps(self: world.Binary), "setup-clients");

        code.Should().Be(0);
        output.Action("claude", "hook").Should().Be("none");
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Single()["command"]!.GetValue<string>().Should().Be(first);
    }
}
