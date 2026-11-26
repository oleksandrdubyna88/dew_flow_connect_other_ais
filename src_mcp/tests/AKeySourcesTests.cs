using System.Text.Json;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t2, the owner's addendum of 2026-11-23 — where keys come from besides <c>--keys add</c>: the <c>COAI_CLAUDE_KEY_&lt;label&gt;</c> variables, a file at a path the person chose (env lines or the creds JSON), and the one-shot
/// creds import; each synced into the pool (new → added, changed → replaced, missing → disabled; only the keys of that source). And <c>--keys scan</c> (d6).
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
[Collection("key-process-environment")]
public sealed class AKeySourcesTests
{
    private static string Creds(params (string Label, string Account, string Tag)[] keys) =>
        $$"""{"version":1,"keys":[{{string.Join(',', keys.Select(key => $$"""{"label":"{{key.Label}}","account":"{{key.Account}}","token":"{{KeyWorld.Token(key.Tag)}}"}"""))}}]}""";

    private static string EnvLines(params (string Label, string Tag, string Account)[] keys) =>
        string.Join('\n', keys.SelectMany(key => new[] { $"COAI_CLAUDE_KEY_{key.Label}={KeyWorld.Token(key.Tag)}" }.Concat(key.Account.Length > 0 ? [$"COAI_CLAUDE_ACCOUNT_{key.Label}={key.Account}"] : []))) + "\n";

    private static JsonElement Rows(KeysRun run) => run.List().GetProperty("rows");

    private static string StateOf(KeysRun run, string label) =>
        Rows(run).EnumerateArray().Single(row => row.GetProperty("label").GetString() == label).GetProperty("state").GetString()!;

    // ---------- the environment ----------

    [Fact]
    public void TheEnvironment_IsSyncedIntoThePool_NewAdded_ChangedReplaced_MissingDisabled()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["COAI_CLAUDE_KEY_Alpha"] = KeyWorld.Token("a");
        run.Variables["COAI_CLAUDE_ACCOUNT_alpha"] = "owner@example.com";
        run.Variables["COAI_CLAUDE_KEY_beta"] = KeyWorld.Token("b");
        world.AddKey("by hand", "h");

        var first = run.Do(string.Empty, "sync", "--json");

        first.Code.Should().Be(0, first.Err);
        first.Out.Should().NotContain(KeyWorld.Sentinel);
        var rows = Rows(run).EnumerateArray().ToList();
        rows.Select(row => row.GetProperty("label").GetString()).Should().BeEquivalentTo(["by hand", "alpha", "beta"], "a label is lower-cased");
        rows.Single(row => row.GetProperty("label").GetString() == "alpha").GetProperty("account").GetString().Should().Be("owner@example.com");
        rows.Single(row => row.GetProperty("label").GetString() == "alpha").GetProperty("source").GetString().Should().Be("env");

        run.Variables["COAI_CLAUDE_KEY_Alpha"] = KeyWorld.Token("a2");
        run.Variables.Remove("COAI_CLAUDE_KEY_beta");
        run.Do(string.Empty, "sync");

        StateOf(run, "beta").Should().Be("disabled", "a key the source no longer lists is disabled, not deleted");
        StateOf(run, "by hand").Should().Be("enabled", "the keys of another source are not touched");
        Rows(run).EnumerateArray().Select(row => row.GetProperty("id").GetString()).Should().Contain(KeyWorld.IdOf(KeyWorld.Token("a2"))).And.NotContain(KeyWorld.IdOf(KeyWorld.Token("a")), "a changed text is a replaced key");
        File.Exists(world.KeyFile("a")).Should().BeFalse("the replaced pair is deleted");

        run.Variables["COAI_CLAUDE_KEY_beta"] = KeyWorld.Token("b");
        run.Do(string.Empty, "sync");
        StateOf(run, "beta").Should().Be("enabled", "a key that comes back is enabled again: a sync disabled it");
    }

    [Fact]
    public void ASyncDoesNotEnableAKeyAPersonDisabled()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["COAI_CLAUDE_KEY_alpha"] = KeyWorld.Token("a");
        run.Do(string.Empty, "sync");
        run.Do(string.Empty, "disable", "--id", KeyWorld.IdOf(KeyWorld.Token("a")));

        run.Do(string.Empty, "sync");

        StateOf(run, "alpha").Should().Be("disabled");
    }

    [Fact]
    public void AnEnvironmentVariableWithABadLabel_IsRefusedByPosition_NeverEchoed()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["COAI_CLAUDE_KEY_bad label!"] = KeyWorld.Token("a");
        run.Variables["COAI_CLAUDE_KEY_good"] = KeyWorld.Token("b");

        var (code, output, said) = run.Do(string.Empty, "sync", "--json");

        code.Should().Be(0, said);
        (output + said).Should().NotContain(KeyWorld.Sentinel);
        output.Should().Contain("\"refused\":[{\"line\":1");
        Rows(run).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AStartThatHasNothingConfigured_MakesNoFolder_AndOneThatHasASource_SyncsIt()
    {
        using var world = new KeyWorld();
        var log = Serilog.Core.Logger.None;

        KeyStartup.Sync(world.DataDir, _ => null, log);
        Directory.Exists(Path.Combine(world.DataDir, "keys")).Should().BeFalse("nothing is configured: the disk is not touched");

        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, EnvLines(("one", "a", string.Empty)));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        KeyStartup.Sync(world.DataDir, name => name == "COAI_CLAUDE_KEYS_FILE" ? file : null, log);

        new KeysRun(world).List().GetProperty("rows").GetArrayLength().Should().Be(1);
    }

    // ---------- a file at a path the person chose ----------

    [Fact]
    public void AKeysFile_InEnvLines_IsSynced_WithAccounts_CommentsAndBlankLines()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, "# the owner's keys\n\n" + EnvLines(("one", "a", "me@example.com"), ("two", "b", string.Empty)));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;

        var synced = run.Do(string.Empty, "sync", "--json");

        synced.Code.Should().Be(0, synced.Err);
        synced.Out.Should().NotContain(KeyWorld.Sentinel);
        var rows = Rows(run).EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        rows.Single(row => row.GetProperty("label").GetString() == "one").GetProperty("account").GetString().Should().Be("me@example.com");
        rows.All(row => row.GetProperty("source").GetString() == "file:" + file).Should().BeTrue();
    }

    [Fact]
    public void AKeyWhoseAccountChanges_LosesTheCooldownAnotherKeyOfTheOldAccountGaveIt_ButTheKeyThatHitTheLimitKeepsItsOwn()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, EnvLines(("one", "a", "old@example.com"), ("two", "b", "old@example.com")));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;
        run.Do(string.Empty, "sync").Code.Should().Be(0);
        new KeyPool(world.Access()).Record(KeyWorld.IdOf(KeyWorld.Token("a")), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 3h"));
        CooldownOf(run, "two").Should().NotBeNull("the limit was the old account's, which the key shared then");

        File.WriteAllText(file, EnvLines(("one", "a", "new@example.com"), ("two", "b", "new@example.com")));
        var synced = run.Do(string.Empty, "sync");

        synced.Code.Should().Be(0, synced.Err);
        CooldownOf(run, "two").Should().BeNull("the key was wrongly put on the old account: that limit was not its");
        CooldownOf(run, "one").Should().NotBeNull("the vendor stopped THIS key: its own cooldown stays whatever account it is filed under");
    }

    [Fact]
    public void AKeyWhoseAccountOnlyChangesCase_KeepsTheCooldownItWasGiven()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, EnvLines(("one", "a", "me@example.com"), ("two", "b", "me@example.com")));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;
        run.Do(string.Empty, "sync").Code.Should().Be(0);
        new KeyPool(world.Access()).Record(KeyWorld.IdOf(KeyWorld.Token("a")), new KeyLaunchReport(KeyLaunchEnd.Limit, "Resets in 3h"));

        File.WriteAllText(file, EnvLines(("one", "a", "me@example.com"), ("two", "b", "ME@example.com")));
        run.Do(string.Empty, "sync").Code.Should().Be(0);

        CooldownOf(run, "two").Should().NotBeNull("the same account written another way is the same account");
    }

    private static string? CooldownOf(KeysRun run, string label) =>
        Rows(run).EnumerateArray().Single(row => row.GetProperty("label").GetString() == label).TryGetProperty("cooldownUntilUtc", out var until) && until.ValueKind == JsonValueKind.String ? until.GetString() : null;

    [Fact]
    public void TheDeviceSettingKeysFile_IsReadToo_WhenTheVariableIsNotSet()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, EnvLines(("one", "a", string.Empty)));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        run.Settings["keysFile"] = file;

        run.Do(string.Empty, "sync").Code.Should().Be(0);

        Rows(run).GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData(UnixFileMode.GroupRead)]
    [InlineData(UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.GroupWrite)]
    public void AKeysFileThatGroupOrOtherCanTouch_IsRefusedWhole_AndTheKeysItListedBeforeStayAsTheyWere(UnixFileMode extra)
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, EnvLines(("one", "a", string.Empty)));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;
        run.Do(string.Empty, "sync");
        File.WriteAllText(file, EnvLines(("two", "b", string.Empty)));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | extra);

        var (code, output, said) = run.Do(string.Empty, "sync", "--json");

        code.Should().Be(0, said);
        output.Should().Contain("readable by group or other").And.Contain("nothing was read");
        output.Should().NotContain(KeyWorld.Sentinel);
        Rows(run).GetArrayLength().Should().Be(1, "nothing of the refused file was imported");
        StateOf(run, "one").Should().Be("enabled", "a source that could not be read changes nothing: its keys are not disabled");
    }

    [Fact]
    public void AKeysFileThatIsNotThere_IsARefusal_NotADisableOfItsKeys()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, EnvLines(("one", "a", string.Empty)));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;
        run.Do(string.Empty, "sync");
        File.Delete(file);

        var (_, output, _) = run.Do(string.Empty, "sync", "--json");

        output.Should().Contain("not there");
        StateOf(run, "one").Should().Be("enabled");
    }

    [Fact]
    public void AnEnvLineThatIsNothingKnown_IsRefusedByItsNumber_AndNeverEchoed()
    {
        using var world = new KeyWorld();
        var file = Path.Combine(world.DataDir, "keys.env");
        File.WriteAllText(file, $"# fine\nexport COAI_CLAUDE_KEY_x={KeyWorld.Token("x")}\nCOAI_CLAUDE_KEY_ok={KeyWorld.Token("a")}\njust some words {KeyWorld.Token("y")}\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var run = new KeysRun(world);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;

        var (_, output, said) = run.Do(string.Empty, "sync", "--json");

        (output + said).Should().NotContain(KeyWorld.Sentinel);
        output.Should().Contain("\"line\":2").And.Contain("\"line\":4");
        Rows(run).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AKeysFile_InTheCredsJsonShape_IsDetected_AndSynced()
    {
        using var world = new KeyWorld();
        var file = Path.Combine(world.DataDir, "keys.json");
        File.WriteAllText(file, Creds(("alpha", "a@example.com", "a"), ("beta", "a@example.com", "b")));
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var run = new KeysRun(world);
        run.Settings["COAI_CLAUDE_KEYS_FILE"] = file;

        run.Do(string.Empty, "sync").Code.Should().Be(0);

        Rows(run).GetArrayLength().Should().Be(2);
        Rows(run).EnumerateArray().All(row => row.GetProperty("account").GetString() == "a@example.com").Should().BeTrue();
    }

    // ---------- the creds import ----------

    [Fact]
    public void TheCredsImport_ReadsTheEntrysBody_AddsTheKeys_AndAReImportDisablesWhatTheEntryNoLongerHas()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        world.AddKey("by hand", "h");

        var first = run.Do(Creds(("alpha", "a@example.com", "a"), ("beta", "b@example.com", "b")), "import", "--format", "creds-json", "--source", "creds", "--json");
        var again = run.Do(Creds(("alpha", "a@example.com", "a")), "import", "--format", "creds-json", "--source", "creds", "--json");

        first.Code.Should().Be(0, first.Err);
        (first.Out + again.Out).Should().NotContain(KeyWorld.Sentinel);
        StateOf(run, "alpha").Should().Be("enabled");
        StateOf(run, "beta").Should().Be("disabled", "a key gone from the entry is disabled, not deleted, on re-import");
        StateOf(run, "by hand").Should().Be("enabled", "only the keys of that source are touched");
        File.Exists(world.KeyFile("b")).Should().BeTrue("disabled, not deleted");
    }

    [Theory]
    [InlineData("""{"version":1,"keys":[],"extra":1}""")]
    [InlineData("""{"version":2,"keys":[]}""")]
    [InlineData("""{"version":1,"keys":[{"label":"a","account":"x","token":"t","note":"n"}]}""")]
    [InlineData("""{"version":1,"keys":[{"label":"a","token":"t"},{"label":"A","token":"u"}]}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("not json")]
    public void TheCredsJson_WithAnUnknownKey_AWrongVersion_OrARepeatedLabel_IsRefusedWhole(string body)
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        world.AddKey("by hand", "h");

        var (code, _, said) = run.Do(body, "import", "--format", "creds-json");

        code.Should().Be(65);
        said.Should().Contain("nothing was read");
        Rows(run).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void TheImportNeverReadsTheCredsCodeAccessKey_AndNoSourceInTheKeysFolderNamesIt()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        run.Variables["CREDSFORDEVS_KEY"] = "SENTINEL-CODE-ACCESS-KEY";
        run.Settings["CREDSFORDEVS_KEY"] = "SENTINEL-CODE-ACCESS-KEY";

        var (code, output, said) = run.Do(Creds(("alpha", "a@example.com", "a")), "import", "--format", "creds-json", "--json");

        code.Should().Be(0, said);
        var everything = string.Join('\n', Directory.GetFiles(world.DataDir, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(".key", StringComparison.Ordinal)).Select(File.ReadAllText)) + output + said;
        everything.Should().NotContain("SENTINEL-CODE-ACCESS-KEY");
        var keysFolder = Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Server", "Keys");
        Directory.GetFiles(keysFolder, "*.cs").Where(path => File.ReadAllText(path).Contains("CREDSFORDEVS_KEY", StringComparison.Ordinal)).Should().BeEmpty("coai never stores the creds code-access key, and never reads it");
    }

    [Theory]
    [InlineData("local")]
    [InlineData("env")]
    [InlineData("file:/x")]
    [InlineData("Bad Name")]
    public void AnImportSource_CannotBeTheNameOfTheKeysAPersonAdded_OrOfAnotherSource(string source)
    {
        using var world = new KeyWorld();

        var (code, _, said) = new KeysRun(world).Do(Creds(("alpha", "a@example.com", "a")), "import", "--format", "creds-json", "--source", source);

        code.Should().Be(65, "a sync of `local` would disable every key a person added by hand");
        said.Should().Contain("--source");
    }

    [Fact]
    public void TheEnvFormatOfImport_WorksOnAnyDevice()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);

        var (code, _, said) = run.Do(EnvLines(("one", "a", "x@example.com")), "import", "--format", "env", "--json");

        code.Should().Be(0, said);
        Rows(run).GetArrayLength().Should().Be(1);
    }

    // ---------- the COAI_ names reach no child ----------

    [Fact]
    public void TheKeyVariables_AreOnNoChildsAllowList_NotTheRunnersFile_NorTheMinimalEnvironment()
    {
        string[] names = ["COAI_CLAUDE_KEY_alpha", "COAI_CLAUDE_ACCOUNT_alpha", "COAI_CLAUDE_KEYS_FILE"];

        names.Where(RoleRunEnvironment.Allowed).Should().BeEmpty("the runner's env.json never holds a key variable");
        var captured = RoleRunEnvironment.Capture(names.ToDictionary(name => name, _ => KeyWorld.Token("a"), StringComparer.Ordinal));
        captured.Variables.Should().BeNullOrEmpty();
        RoleRunEnvironment.Allowed("COAI_ROLE_CLAUDE_PERSON_SIGN_IN").Should().BeTrue("the switch of the empty pool does reach it (t5), the COAI_ROLE_ prefix");
        CoaiMcp.Runners.Processes.ProcessEnvironment.Minimal.Where(name => name.StartsWith("COAI_", StringComparison.Ordinal)).Should().BeEmpty();
        CoaiMcp.Runners.Processes.ProcessEnvironment.Passthrough.Where(name => name.StartsWith("COAI_", StringComparison.Ordinal)).Should().BeEmpty();
    }

    // ---------- d6: scan ----------

    [Fact]
    public void D6_AScan_FindsAFileThatHoldsTheExactText_AndPrintsThePathAndTheKeyId_NeverTheText()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        world.AddKey("one", "a");
        world.AddKey("two", "b");
        using var scanned = TempDir.For("coai-keyscan-");
        Directory.CreateDirectory(scanned.At("deep", "er"));
        File.WriteAllText(scanned.At("clean.txt"), "nothing here");
        File.WriteAllText(scanned.At("deep", "er", "leak.log"), $"before {KeyWorld.Token("b")} after");

        var (code, output, said) = run.Do(string.Empty, "scan", "--path", scanned.Path, "--json");

        code.Should().Be(0, said);
        output.Should().NotContain(KeyWorld.Sentinel);
        using var answer = JsonDocument.Parse(output);
        var hits = answer.RootElement.GetProperty("hits").EnumerateArray().ToList();
        hits.Should().ContainSingle();
        hits[0].GetProperty("path").GetString().Should().Be(scanned.At("deep", "er", "leak.log"));
        hits[0].GetProperty("keyId").GetString().Should().Be(KeyWorld.IdOf(KeyWorld.Token("b")));
        answer.RootElement.GetProperty("scanned").GetInt32().Should().Be(2);
    }

    [Fact]
    public void D6_AScanOfACleanFolder_AnswersNoHit()
    {
        using var world = new KeyWorld();
        world.AddKey("one", "a");
        using var scanned = TempDir.For("coai-keyscan-");
        File.WriteAllText(scanned.At("clean.txt"), "nothing here but " + KeyWorld.Sentinel.ToLowerInvariant());

        var (code, output, _) = new KeysRun(world).Do(string.Empty, "scan", "--path", scanned.Path);

        code.Should().Be(0);
        output.Should().Contain("no hit");
    }

    [Fact]
    public void D6_AScanOfTheDataFolder_DoesNotFindTheKeysInTheirOwnFiles_ButFindsACopyAnywhereElseInIt()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        world.AddKey("one", "a");
        Directory.CreateDirectory(Path.Combine(world.DataDir, "logs"));

        run.Do(string.Empty, "scan", "--path", world.DataDir).Out.Should().Contain("no hit");
        File.WriteAllText(Path.Combine(world.DataDir, "logs", "oops.log"), KeyWorld.Token("a"));

        run.Do(string.Empty, "scan", "--path", world.DataDir, "--json").Out.Should().Contain("oops.log");
    }

    [Fact]
    public void D6_ATextCutByAChunkEdge_IsStillFound_AndALinkIsNotFollowed()
    {
        using var world = new KeyWorld();
        var run = new KeysRun(world);
        world.AddKey("one", "a");
        using var scanned = TempDir.For("coai-keyscan-");
        using var outside = TempDir.For("coai-keyscan-outside-");
        var text = KeyWorld.Token("a");
        // The text starts 20 bytes before the 64 KiB edge of the first chunk.
        File.WriteAllText(scanned.At("edge.bin"), new string('.', 64 * 1024 - 20) + text + new string('.', 100));
        File.WriteAllText(Path.Combine(outside.Path, "outside.txt"), text);
        Directory.CreateSymbolicLink(scanned.At("link"), outside.Path);

        var hits = JsonDocument.Parse(run.Do(string.Empty, "scan", "--path", scanned.Path, "--json").Out).RootElement.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("path").GetString()).ToList();

        hits.Should().Equal(scanned.At("edge.bin"));
    }

    [Fact]
    public void D6_AScanOfAPathThatIsNotThere_IsRefused()
    {
        using var world = new KeyWorld();

        new KeysRun(world).Do(string.Empty, "scan", "--path", Path.Combine(world.DataDir, "missing")).Code.Should().Be(65);
        new KeysRun(world).Do(string.Empty, "scan").Code.Should().Be(65);
    }
}
