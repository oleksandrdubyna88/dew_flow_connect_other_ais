using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The part of the hardening tests (the security judgement of s7 part 1) that covers: M2.</summary>
public sealed partial class AKeyHardeningTests
{
    // ---------- must 2: shared mode needs the device setting AND the bit ----------

    [Fact]
    public void M2_ASetgidBitAlone_IsNotASharedInstance_TheFolderIs0700AndTheKeyIs0600()
    {
        using var world = new KeyWorld();
        Assert.SkipUnless(SetgidSupport.Real.TryApply(world.DataDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.SetGroup, out var why), why);

        world.AddKey("A", "a");

        File.GetUnixFileMode(world.KeysRoot).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, "a bit inherited from the parent is not the owner's decision to share");
        File.GetUnixFileMode(world.KeyFile("a")).Should().Be(Private);
        world.Admin.SharedSentence.Should().Contain("COAI_KEYS_SHARED").And.Contain("stay private", "the bit alone is said not to share, so a person is told why the group cannot read the keys");
    }

    [Fact]
    public void M2_OnASharedInstance_TheFirstAdd_AnImport_AndASync_EachSayEveryMemberOfTheGroupReadsEveryKey()
    {
        using var world = new KeyWorld(shared: true);
        Assert.SkipUnless(SetgidSupport.Real.TryApply(world.DataDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.SetGroup, out var why), why);
        var run = new KeysRun(world);
        run.Settings["COAI_KEYS_SHARED"] = "1";
        const string Said = "every member of its group can read every key";

        run.Do(KeyWorld.Token("a") + "\n", "add", "--label", "one").Err.Should().Contain(Said);
        run.Do($"{{\"label\":\"two\",\"token\":\"{KeyWorld.Token("b")}\"}}\n", "import").Err.Should().Contain(Said);
        run.Variables["COAI_CLAUDE_KEY_three"] = KeyWorld.Token("c");
        run.Do(string.Empty, "sync").Err.Should().Contain(Said);
        File.GetUnixFileMode(world.KeyFile("a")).Should().Be(Private | UnixFileMode.GroupRead | UnixFileMode.GroupWrite, "with the setting AND the bit, the instance is shared");
    }

    [Fact]
    public void M2_AStartOnASharedInstance_SaysSoInTheLog_AndAnIgnoredBitIsSaidToo()
    {
        using var world = new KeyWorld();
        Assert.SkipUnless(SetgidSupport.Real.TryApply(world.DataDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.SetGroup, out var why), why);
        var variables = new Dictionary<string, string> { ["COAI_CLAUDE_KEY_one"] = KeyWorld.Token("a") };

        string StartWith(string shared)
        {
            var lines = new List<string>();
            var log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new Collected(lines)).CreateLogger();
            KeyStartup.Sync(world.DataDir, name => name == "COAI_KEYS_SHARED" ? shared : null, log, () => variables, _ => { });

            return string.Join('\n', lines);
        }

        StartWith("0").Should().Contain("stay private to this user", "the bit without the setting is said not to share");
        StartWith("1").Should().Contain("every member of its group can read every key");
    }

    private sealed class Collected(List<string> lines) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => lines.Add(logEvent.RenderMessage());
    }

    [Fact]
    public void M1_AfterTheStartSync_TheEnvKeysAreNoLongerInTheProcessEnvironment()
    {
        using var world = new KeyWorld();
        var variables = new Dictionary<string, string>
        {
            ["COAI_CLAUDE_KEY_one"] = KeyWorld.Token("a"),
            ["COAI_CLAUDE_ACCOUNT_one"] = "someone@example.com",
            ["UNRELATED_NAME"] = "x",
        };
        var removed = new List<string>();

        KeyStartup.Sync(world.DataDir, _ => null, Serilog.Core.Logger.None, () => variables, removed.Add);

        removed.Should().BeEquivalentTo("COAI_CLAUDE_KEY_one", "COAI_CLAUDE_ACCOUNT_one");
        world.Admin.List(false).Value!.Rows.Should().ContainSingle("the key was synced before the variable was removed");
    }
}
