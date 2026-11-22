using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Scenarios 1, 2 and 3 (hook halves) of the installer, v2 E3.S7 t2: a fresh HOME gets ONE handler of the contract's shape; a second run changes nothing; and another tool's hooks, keys and spellings
/// are as they were — the text diff is added lines only — and no PreToolUse or AskUserQuestion hook is ever written.
/// </summary>
public sealed class AClaudeHookIsAddedAsLinesTests
{
    [Fact]
    public async Task OnAFreshHome_OneHandlerOfTheContractsShapeIsWritten()
    {
        using var world = InstallWorld.Create(codex: false);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Action("claude", "hook").Should().Be("add");
        var settings = JsonNode.Parse(world.ClaudeSettingsText)!;
        settings["hooks"]!.AsObject().Select(one => one.Key).Should().Equal("SessionStart");
        var groups = settings["hooks"]!["SessionStart"]!.AsArray();
        groups.Should().HaveCount(1, "one own group");
        var handlers = ClaudeHookFixtures.Handlers(groups[0]).ToList();
        handlers.Should().HaveCount(1);
        handlers[0]["type"]!.GetValue<string>().Should().Be("command");
        handlers[0]["command"]!.GetValue<string>().Should().Be(world.Binary);
        handlers[0]["args"]!.AsArray().Select(arg => arg!.GetValue<string>()).Should().Equal("--roles", "--format", "claude-hook");
        handlers[0]["timeout"]!.GetValue<int>().Should().Be(10);
        var manifest = File.ReadAllText(Path.Combine(world.BackupFolders.Single(), "manifest.json"));
        manifest.Should().Contain("\"kind\": \"hook\"", "the hook is a line of the manifest, so it can be restored and removed");
    }

    [Fact]
    public async Task ASecondRun_IsNoneForTheHookToo_AndWritesNothing()
    {
        using var world = InstallWorld.Create(codex: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        var text = world.ClaudeSettingsText;
        var tree = world.HomeHash();
        var calls = world.Calls.Count;

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0);
        output.Action("claude", "hook").Should().Be("none");
        world.ClaudeSettingsText.Should().Be(text);
        world.HomeHash().Should().Be(tree);
        world.Calls.Count.Should().Be(calls);
        ClaudeHookFixtures.OurHandlers(world.ClaudeSettingsText).Should().HaveCount(1, "a run again never makes a second group");
    }

    [Theory]
    [InlineData(ClaudeHookFixtures.Lived, "a SessionStart list that already holds another tool's group")]
    [InlineData(ClaudeHookFixtures.LivedWithoutSessionStart, "hooks without a SessionStart")]
    [InlineData(ClaudeHookFixtures.LivedWithoutHooks, "no hooks at all")]
    public async Task AnotherToolsHooksAndKeys_AreAsTheyWere_AndTheDiffIsAddedLinesOnly(string fixture, string why)
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, fixture);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err + why);
        var after = world.ClaudeSettingsText;
        ClaudeHookFixtures.WithoutOurs(after).ToJsonString().Should().Be(JsonNode.Parse(fixture)!.ToJsonString(), $"deep-equal but for our group — {why}");
        var added = ClaudeHookFixtures.AddedLines(fixture, after);
        added.Should().NotBeEmpty();
        string.Join('\n', added).Should().Contain(world.Binary).And.Contain("--roles");
        after.Should().Contain(fixture.Contains("é<1e5>") ? "é<1e5>" : "model", "a non-ASCII letter and a < are not turned into escapes").And.Contain("\"ratio\": 1e5", "a number is not respelled");
        ClaudeHookFixtures.OurHandlers(after).Should().HaveCount(1);
    }

    [Fact]
    public async Task AGroupAlreadyInSessionStart_IsNotMoved_AndOursComesFirst()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeSettings, ClaudeHookFixtures.Lived);

        await world.RunAsync("--binary", world.Binary);

        var groups = JsonNode.Parse(world.ClaudeSettingsText)!["hooks"]!["SessionStart"]!.AsArray();
        groups.Should().HaveCount(2);
        ClaudeHookFixtures.Handlers(groups[0]).Single()["command"]!.GetValue<string>().Should().Be(world.Binary);
        ClaudeHookFixtures.Handlers(groups[1]).Single()["command"]!.GetValue<string>().Should().Be("node");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ClaudeHookFixtures.Lived)]
    [InlineData(ClaudeHookFixtures.LivedWithoutHooks)]
    public async Task NoPreToolUseOrAskUserQuestionHook_IsEverWritten(string? fixture)
    {
        using var world = InstallWorld.Create();
        if (fixture is not null)
        {
            world.Put(world.ClaudeSettings, fixture);
        }

        var before = fixture is null ? null : JsonNode.Parse(fixture)!["hooks"]?["PreToolUse"]?.ToJsonString();

        await world.RunAsync("--binary", world.Binary);
        await world.RunAsync("--binary", world.AnotherBinary("opt", "second"));

        var hooks = JsonNode.Parse(world.ClaudeSettingsText)!["hooks"]!.AsObject();
        hooks["PreToolUse"]?.ToJsonString().Should().Be(before, "a guard that was there is as it was, and none was made");
        world.ClaudeSettingsText.Should().NotContain("AskUserQuestion");
        hooks.Select(one => one.Key).Except(["PreToolUse"]).Should().Equal("SessionStart");
    }

    [Fact]
    public async Task ATrailingNewline_ACrLfFile_AndAByteOrderMark_AreKept()
    {
        using var world = InstallWorld.Create(codex: false);
        var crlf = ClaudeHookFixtures.Lived.Replace("\n", "\r\n") + "\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(world.ClaudeSettings)!);
        File.WriteAllBytes(world.ClaudeSettings, [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. System.Text.Encoding.UTF8.GetBytes(crlf)]);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var bytes = File.ReadAllBytes(world.ClaudeSettings);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().EndWith("}\r\n");
        text.Replace("\r\n", string.Empty).Should().NotContain("\n", "every line end of the added lines is the file's own");
        ClaudeHookFixtures.OurHandlers(text).Should().HaveCount(1);
    }
}
