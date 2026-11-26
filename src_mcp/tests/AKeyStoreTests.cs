using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t1 — the key store of a device: one file per key under <c>&lt;dataDir&gt;/keys/claude</c>, an id from a hash prefix, metadata beside it, modes read back, a lock (KEY_POOL_SPEC.md section 3).
/// Scenarios K1 and K3 of the spec; the numbers in the names are the task's <c>dod</c> ids.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class AKeyStoreTests
{
    private static readonly UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly UnixFileMode PrivateFolder = PrivateFile | UnixFileMode.UserExecute;

    // ---------- d1: the pair of files, the id, the same text twice, the 201st key ----------

    [Fact]
    public void D1_AnAdd_WritesTheTextOnlyIntoTheKeyFile_AndTheMetadataWithoutIt()
    {
        using var world = new KeyWorld();

        var row = world.AddKey("work laptop", "a");

        var id = KeyWorld.IdOf(KeyWorld.Token("a"));
        row.Id.Should().Be(id, "the id is k- and the first 12 hex characters of SHA-256 over the spec's fixed prefix line and the text");
        File.ReadAllText(world.KeyFile("a")).Trim().Should().Be(KeyWorld.Token("a"), "the .key file holds the text and nothing else");
        var meta = File.ReadAllText(world.MetaFile("a"));
        meta.Should().NotContain(KeyWorld.Sentinel, "the metadata file has no text of the key, not even a part");
        using var json = JsonDocument.Parse(meta);
        json.RootElement.GetProperty("label").GetString().Should().Be("work laptop");
        json.RootElement.GetProperty("kind").GetString().Should().Be("claude-setup-token");
        json.RootElement.GetProperty("state").GetString().Should().Be("enabled");
        Directory.GetFiles(world.KeysRoot).Select(Path.GetFileName).Should().BeEquivalentTo([id + ".key", id + ".json", "pool.lock", "pool.json"], "nothing else is left behind: no temp file");
    }

    [Fact]
    public void D1_TheIdIsNotAPlainHashOfTheText()
    {
        var token = KeyWorld.Token("a");

        KeyIds.Of(token).Should().Be(KeyWorld.IdOf(token));
        var plain = "k-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))[..12];
        KeyIds.Of(token).Should().NotBe(plain, "the Team server and the hub store plain hashes of their own tokens; an id here is not a handle for those");
    }

    [Fact]
    public void D1_TheSameTextTwice_IsRefusedWithTheLabelItHas()
    {
        using var world = new KeyWorld();
        world.AddKey("first", "a");

        var again = world.Admin.Add(new KeyInput(1, "second", KeyWorld.Token("a"), string.Empty, KeySources.Local));

        again.Ok.Should().BeFalse();
        again.Refusal.Should().Contain("already in the pool as first");
        again.Refusal.Should().NotContain(KeyWorld.Sentinel);
        world.Admin.List(false).Value!.Rows.Should().HaveCount(1);
    }

    [Fact]
    public void D1_The201stKey_IsRefused()
    {
        using var world = new KeyWorld();
        var inputs = Enumerable.Range(0, KeyAdmin.MaxKeys).Select(number => new KeyInput(number + 1, $"key {number}", KeyWorld.Token($"n{number}"), string.Empty, KeySources.Local)).ToList();

        var imported = world.Admin.Import(inputs);

        imported.Value!.Added.Should().Be(200);
        var last = world.Admin.Add(new KeyInput(1, "one too many", KeyWorld.Token("zzz"), string.Empty, KeySources.Local));
        last.Ok.Should().BeFalse();
        last.Refusal.Should().Contain("200");
        world.Admin.List(false).Value!.Rows.Should().HaveCount(200);
    }

    // ---------- d2: modes, read back ----------

    [Fact]
    public void D2_TheFolderIs0700_TheFilesAre0600_ReadBackFromTheDisk()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "modes are a Unix thing; the ACL leg is PrivateFolder's");
        using var world = new KeyWorld();
        world.AddKey("one", "a");

        File.GetUnixFileMode(Path.Combine(world.DataDir, "keys")).Should().Be(PrivateFolder);
        File.GetUnixFileMode(world.KeysRoot).Should().Be(PrivateFolder);
        foreach (var file in Directory.GetFiles(world.KeysRoot))
        {
            File.GetUnixFileMode(file).Should().Be(PrivateFile, $"{Path.GetFileName(file)} is private");
        }
    }

    [Fact]
    public void D2_AFolderThatDoesNotKeepModes_RefusesTheAdd_WithItsNameAndTheCure_AndLeavesNoKeyFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "modes are a Unix thing");
        using var world = new KeyWorld(new KeyFolderSeams(_ => UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));

        var added = world.Admin.Add(new KeyInput(1, "one", KeyWorld.Token("a"), string.Empty, KeySources.Local));

        added.Ok.Should().BeFalse();
        added.Code.Should().Be(KeyExit.Io, "a data folder that cannot be used is 74, not a wrong argument");
        added.Refusal.Should().Contain(Path.Combine(world.DataDir, "keys"), "the refusal names the folder");
        added.Refusal.Should().Contain("Linux file system", "and says the cure");
        added.Refusal.Should().NotContain(KeyWorld.Sentinel);
        Directory.GetFiles(world.DataDir, "*.key", SearchOption.AllDirectories).Should().BeEmpty("no key was written into a folder that cannot protect it");
    }

    [Fact]
    public void D2_AFileThatDidNotKeepItsMode_IsRefusedAndRemoved()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "modes are a Unix thing");
        // The folders keep modes (the real read-back); only a KEY file reads back wide: the seam answers by name.
        using var world = new KeyWorld(new KeyFolderSeams(path => path.EndsWith(".key", StringComparison.Ordinal) ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead : File.GetUnixFileMode(path)));

        var added = world.Admin.Add(new KeyInput(1, "one", KeyWorld.Token("a"), string.Empty, KeySources.Local));

        added.Ok.Should().BeFalse();
        added.Refusal.Should().Contain("does not keep file modes");
        Directory.GetFiles(world.KeysRoot, "k-*").Should().BeEmpty("neither half of the pair is left behind");
    }

    [Fact]
    public void D2_ASharedInstance_HasFoldersOf2770_AndFilesOf0660_AndTheFirstAddSaysEveryMemberCanRead()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "modes are a Unix thing");
        using var world = new KeyWorld(shared: true);
        Assert.SkipUnless(SetgidSupport.Real.TryApply(world.DataDir, PrivateFolder | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup, out var why), why);

        world.AddKey("one", "a");

        var folder = PrivateFolder | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup;
        File.GetUnixFileMode(world.KeysRoot).Should().Be(folder);
        File.GetUnixFileMode(world.KeyFile("a")).Should().Be(PrivateFile | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
        File.GetUnixFileMode(world.MetaFile("a")).Should().Be(PrivateFile | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
        world.Admin.SharedSentence.Should().Contain("every member of its group can read every key");
    }

    [Fact]
    public void D2_AKeyFileWithAWiderMode_IsNotUsed_AndTheRowSaysSo()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "modes are a Unix thing");
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        File.SetUnixFileMode(world.KeyFile("a"), PrivateFile | UnixFileMode.OtherRead);

        var row = world.Admin.List(false).Value!.Rows.Single();

        row.Problem.Should().Be("wide-mode");
        row.Usable.Should().BeFalse();
    }

    // ---------- d3: relabel, disable, enable, remove, the label rules ----------

    [Fact]
    public void D3_Relabel_ChangesTheMetadataAlone_AndTheKeyFileIsTheSameBytes()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("old", "a");
        var before = File.ReadAllBytes(world.KeyFile("a"));

        var relabelled = world.Admin.Relabel(row.Id, "new label");

        relabelled.Value!.Label.Should().Be("new label");
        File.ReadAllBytes(world.KeyFile("a")).Should().Equal(before);
        world.Admin.List(false).Value!.Rows.Single().Label.Should().Be("new label");
    }

    [Fact]
    public void D3_DisableAndEnable_ChangeTheStateOnly_AndEnableEndsDeadAndACooldown()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("one", "a");

        world.Admin.Disable(row.Id).Value!.State.Should().Be("disabled");
        world.Admin.Enable(row.Id).Value!.State.Should().Be("enabled");
        var meta = world.MetaFile("a");
        File.WriteAllText(meta, File.ReadAllText(meta).Replace("\"state\": \"enabled\"", "\"state\": \"dead\", \"deadReason\": \"auth\"", StringComparison.Ordinal));
        world.Admin.List(false).Value!.Rows.Single().State.Should().Be("dead");

        var enabled = world.Admin.Enable(row.Id).Value!;

        enabled.State.Should().Be("enabled");
        enabled.DeadReason.Should().BeEmpty();
        enabled.CooldownUntilUtc.Should().BeNull();
    }

    [Fact]
    public void D3_Remove_DeletesThePair()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("one", "a");

        var removed = world.Admin.Remove(row.Id);

        removed.Value!.Removed.Should().Be(row.Id);
        Directory.GetFiles(world.KeysRoot, "k-*").Should().BeEmpty();
        world.Admin.Remove(row.Id).Ok.Should().BeFalse("a second remove finds nothing");
    }

    [Theory]
    [InlineData("", "1 to 40")]
    [InlineData("0123456789012345678901234567890123456789x", "1 to 40")]
    [InlineData("bad/label", "holds only")]
    [InlineData("bad\tlabel", "holds only")]
    public void D3_ALabelOutsideTheRules_IsRefused(string label, string rule)
    {
        using var world = new KeyWorld();

        var added = world.Admin.Add(new KeyInput(1, label, KeyWorld.Token("a"), string.Empty, KeySources.Local));

        added.Ok.Should().BeFalse();
        added.Refusal.Should().Contain(rule);
    }

    [Fact]
    public void D3_ALabelIsUniqueWithoutCase()
    {
        using var world = new KeyWorld();
        world.AddKey("Work", "a");

        var second = world.Admin.Add(new KeyInput(1, "wORk", KeyWorld.Token("b"), string.Empty, KeySources.Local));

        second.Ok.Should().BeFalse();
        second.Refusal.Should().Contain("unique");
        world.Admin.Relabel(world.AddKey("other", "c").Id, "WORK").Ok.Should().BeFalse("a relabel meets the same rule");
    }

    [Fact]
    public void D3_ALabelThatReadsAsASecret_IsRefused_WithTheSentenceThatSaysToRevoke_AndNeverEchoesIt()
    {
        using var world = new KeyWorld();
        var asLabel = string.Concat("sk-", "ant-", "abcdefghijkl");

        var added = world.Admin.Add(new KeyInput(1, asLabel, KeyWorld.Token("a"), string.Empty, KeySources.Local));

        added.Ok.Should().BeFalse();
        added.Refusal.Should().Contain("revoke");
        added.Refusal.Should().NotContain("abcdefghijkl", "a sentence that repeats the label repeats the secret");
    }

    // ---------- d4: the lock ----------

    [Fact]
    public void D4_ACallWaitsForTheLock_AndIsRefusedAsBusyPastTwoSeconds()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        var holder = KeySession.Open(world.Access().Folder, create: false).Value!;
        var watch = Stopwatch.StartNew();

        var busy = world.Admin.List(false);
        watch.Stop();
        holder.Dispose();

        busy.Ok.Should().BeFalse();
        busy.Code.Should().Be(KeyExit.Busy);
        busy.Refusal.Should().Contain("busy");
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1.5)).And.BeLessThan(TimeSpan.FromSeconds(10));
        world.Admin.List(false).Ok.Should().BeTrue("the lock is free again once its holder lets go");
    }

    [Fact]
    public async Task D4_AHolderThatIsKilled_FreesTheLock()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli"), ["pool-lock-hold", Path.Combine(world.KeysRoot, "pool.lock"), "60000"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var holder = Process.Start(info)!;
        try
        {
            (await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)).Should().Be("held");
            world.Admin.List(false).Code.Should().Be(KeyExit.Busy, "another PROCESS holds the lock");
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
            await holder.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        world.Admin.List(false).Ok.Should().BeTrue("the kernel freed the lock when its holder died");
    }

    [Fact]
    public void D4_AKeyFileWithNoMetadata_IsDeletedByTheNextCall_AndMetadataWithNoKeyFileListsAsMissing()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        world.AddKey("two", "b");
        var orphanKey = Path.Combine(world.KeysRoot, KeyWorld.IdOf(KeyWorld.Token("zzz")) + ".key");
        File.WriteAllText(orphanKey, KeyWorld.Token("zzz"));
        var orphanTemp = Path.Combine(world.KeysRoot, "k-000000000000.key.1.deadbeef.tmp");
        File.WriteAllText(orphanTemp, "half");
        File.Delete(world.KeyFile("b"));

        var rows = world.Admin.List(false).Value!.Rows;

        File.Exists(orphanKey).Should().BeFalse("an add that never answered ok left a .key with no .json");
        File.Exists(orphanTemp).Should().BeFalse("a temp file under the lock belongs to a dead writer");
        rows.Should().HaveCount(2);
        rows.Single(row => row.Label == "two").Problem.Should().Be("missing");
        rows.Single(row => row.Label == "two").Usable.Should().BeFalse();
        File.Exists(world.MetaFile("b")).Should().BeTrue("a .json with no .key is kept and shown");
    }

    // ---------- d5: ONE member gives the text ----------

    [Fact]
    public void D5_TheSecret_PrintsAsItsId_InEveryShape()
    {
        var secret = new ClaudeKeySecret("k-0123456789ab", KeyWorld.Token("a"));

        secret.ToString().Should().Be("claude key k-0123456789ab");
        $"{secret}".Should().NotContain(KeyWorld.Sentinel);
        KeyResult<ClaudeKeySecret>.Of(secret).ToString().Should().NotContain(KeyWorld.Sentinel);
        secret.Scrub($"printed {KeyWorld.Token("a")} twice {KeyWorld.Token("a")}").Should().Be("printed [claude key k-0123456789ab] twice [claude key k-0123456789ab]");
    }

    [Fact]
    public void D5_NoJsonContext_HoldsTheSecretOrAnInput()
    {
        KeysJson.Default.GetTypeInfo(typeof(KeyRecord)).Should().NotBeNull("the check must be able to see a registered type");
        KeysAnswerJson.Default.GetTypeInfo(typeof(KeyRow)).Should().NotBeNull();
        foreach (JsonSerializerContext context in new JsonSerializerContext[] { KeysJson.Default, KeysAnswerJson.Default })
        {
            context.GetTypeInfo(typeof(ClaudeKeySecret)).Should().BeNull("no JSON context has the secret type");
            context.GetTypeInfo(typeof(KeyInput)).Should().BeNull("nor the input that holds a text on its way in");
        }
    }

    [Fact]
    public void D5_OnlyTheStoreReadsATokenOut_AndTheListHasNoField_ThatHoldsOne()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");

        var row = world.Admin.List(false).Value!.Rows.Single();
        var members = typeof(KeyRow).GetProperties().Select(property => property.Name).ToList();

        members.Should().NotContain(name => name.Contains("Token", StringComparison.OrdinalIgnoreCase) || name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || name.Contains("Text", StringComparison.OrdinalIgnoreCase));
        row.ToString().Should().NotContain(KeyWorld.Sentinel);
        JsonSerializer.Serialize(row, KeysAnswerJson.Default.KeyRow).Should().NotContain(KeyWorld.Sentinel);
    }

    // ---------- d6: the inventory ----------

    [Fact]
    public void D6_TheDataInventory_NamesKeys_AsNeverMovedAndAlwaysMentioned()
    {
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "data-inventory.json")));

        var entry = inventory.RootElement.GetProperty("entries").EnumerateArray().Single(candidate => candidate.GetProperty("path").GetString() == "keys/");

        entry.GetProperty("move").GetBoolean().Should().BeFalse("a moved data folder must not carry keys to another machine");
        entry.GetProperty("mention").GetBoolean().Should().BeTrue();
    }
}
