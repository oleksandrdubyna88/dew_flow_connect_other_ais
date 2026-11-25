using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The part of the hardening tests (the security judgement of s7 part 1) that covers: Should.</summary>
public sealed partial class AKeyHardeningTests
{
    // ---------- should: the printing of a result, the keys file, the sweep ----------

    [Fact]
    public void S_AKeyResultThatHoldsAText_PrintsNoPartOfIt()
    {
        var holding = KeyResult<string>.Of(KeyWorld.Token("a"));

        holding.ToString().Should().NotContain(KeyWorld.Sentinel);
        KeyResult<ClaudeKeySecret>.Of(new ClaudeKeySecret(KeyWorld.IdOf(KeyWorld.Token("a")), KeyWorld.Token("a"))).ToString().Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public async Task S_AKeysFileThatIsALink_OrAFifo_IsRefusedAtOnce_NeverWaitedFor()
    {
        using var data = TempDir.For("coai-keysfile-");
        var real = Path.Combine(data.Path, "real.env");
        File.WriteAllText(real, $"COAI_CLAUDE_KEY_a={KeyWorld.Token("a")}\n");
        File.SetUnixFileMode(real, Private);
        var link = Path.Combine(data.Path, "link.env");
        File.CreateSymbolicLink(link, real);

        KeySourceReader.FromFile(link, KeyFolderSeams.Real.ModeOf).Ok.Should().BeFalse("a link is not followed");
        KeySourceReader.FromFile(real, KeyFolderSeams.Real.ModeOf).Ok.Should().BeTrue("the companion: the same file, not a link, is read");

        var fifo = Path.Combine(data.Path, "pipe.env");
        using (var made = Process.Start(new ProcessStartInfo("mkfifo", fifo) { UseShellExecute = false })!)
        {
            made.WaitForExit();
        }

        File.SetUnixFileMode(fifo, Private);
        var asked = Task.Run(() => KeySourceReader.FromFile(fifo, KeyFolderSeams.Real.ModeOf), TestContext.Current.CancellationToken);
        var finished = await Task.WhenAny(asked, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == asked;
        if (!finished)
        {
            // Release the reader so the red run does not leave a thread parked for ever.
            using var writer = new FileStream(fifo, FileMode.Open, FileAccess.Write);
        }

        finished.Should().BeTrue("a pipe with no writer would otherwise hold the start for ever");
        (await asked).Ok.Should().BeFalse();
    }

    [Fact]
    public void S_AStartOrSyncWithNoVariableAtAll_DoesNotDisableTheKeysOfTheEnvironmentSource()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["COAI_CLAUDE_KEY_one"] = KeyWorld.Token("a");
        run.Do(string.Empty, "sync").Code.Should().Be(0);
        world.Admin.List(false).Value!.Rows.Should().ContainSingle().Which.State.Should().Be(KeyStates.Enabled);

        run.Variables.Clear();
        run.Do(string.Empty, "sync").Code.Should().Be(0);

        world.Admin.List(false).Value!.Rows.Should().ContainSingle().Which.State.Should().Be(KeyStates.Enabled, "an environment with none of the variables is a process started without them, not a rotation that removed the key");
    }

    [Fact]
    public void S_AKeysFolderThatIsALink_IsRefused_ByAWriteAndByARead()
    {
        using var world = new KeyWorld();
        world.AddKey("A", "a");
        var moved = Path.Combine(world.DataDir, "moved-claude");
        Directory.Move(world.KeysRoot, moved);
        Directory.CreateSymbolicLink(world.KeysRoot, moved);

        world.Admin.Add(new KeyInput(1, "two", KeyWorld.Token("b"), string.Empty, KeySources.Local)).Ok.Should().BeFalse("the store is never made through a link");
        world.Admin.List(false).Ok.Should().BeFalse("nor is it read through one");
    }

    // ---------- the mutations the judge found green ----------

    [Fact]
    public void S_ALimitReasonThatHoldsTheExactText_GluedToALetter_IsScrubbedFromTheNote()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        var pool = new KeyPool(world.Access());
        var token = KeyWorld.Token("a");

        // Glued to a letter, the redactor's own pattern (a word boundary before `sk-`) does not match: only the exact-text scrub removes it.
        var recorded = pool.Record(row.Id, new KeyLaunchReport(KeyLaunchEnd.Limit, "usage limit reached for x" + token + " resets in 1h"));

        recorded.Ok.Should().BeTrue();
        recorded.Value!.Note.Should().NotContain(KeyWorld.Sentinel);
        File.ReadAllText(world.MetaFile("a")).Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void S_EveryJsonContextOfTheAssembly_KnowsNoTypeThatHoldsAKeyText()
    {
        var contexts = typeof(KeyPool).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(System.Text.Json.Serialization.JsonSerializerContext))).ToList();

        contexts.Should().HaveCountGreaterThan(2, "the check must see every context of the assembly, not two of them");
        foreach (var type in contexts)
        {
            var context = (System.Text.Json.Serialization.JsonSerializerContext)type.GetProperty("Default")!.GetValue(null)!;
            context.GetTypeInfo(typeof(ClaudeKeySecret)).Should().BeNull($"{type.Name} must not be able to serialise a key");
            context.GetTypeInfo(typeof(KeyInput)).Should().BeNull($"{type.Name} must not be able to serialise the transient input");
        }
    }

    [Fact]
    public void S_AKeyFile_IsNeverReadableByOthers_EvenForTheMomentItIsBeingWritten()
    {
        using var world = new KeyWorld();
        var seen = new List<UnixFileMode>();
        var folder = new KeyFolder(world.DataDir, KeyFolderSeams.Real, whenCreated: path => seen.Add(File.GetUnixFileMode(path)));
        Directory.CreateDirectory(world.KeysRoot);

        folder.WritePrivate(Path.Combine(world.KeysRoot, "k-000000000000.key"), "x\n", durable: true);

        seen.Should().ContainSingle().Which.Should().Be(Private, "the temp file is made 0600 from its first byte, whatever the umask says");
    }
}
