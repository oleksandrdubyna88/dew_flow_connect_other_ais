using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A coai-owned <c>VIBE_HOME</c> holds only what Vibe itself writes, or the launch is refused naming the path (D17 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; the inventory is research/RESULTS_vibe_confinement.md §22 and §24).
/// </summary>
/// <remarks>
/// The manifest is built here from the SHIPPED one's own lists (its pinned prefix and varying names), with one pinned
/// file whose bytes this test controls — so the varying rules under test are the product's, and a pinned hash can be
/// right or wrong on purpose. The admitted inventory is the positive companion every refusal below needs: with it
/// green, a refusal test that goes red is about the file it adds, not about a fixture the check rejects anyway.
/// </remarks>
public sealed class VibeHomeTests : IDisposable
{
    private const string PluginPath = "logs/session/plugins/blobs/aa/plugin-blob";
    private static readonly byte[] PluginBytes = Encoding.UTF8.GetBytes("the bundled plugin bytes\n");

    private readonly TempDir _home = TempDir.For("coai-vibe-home-");

    public void Dispose() => _home.Dispose();

    private static VibeHomeManifest Shipped =>
        ((VibeManifestLoad.Loaded)VibeHomeManifest.Builtin).Manifest;

    private static VibeHomeEntry Entry =>
        new("9.9.9", [new VibePinnedFile(PluginPath, Convert.ToHexStringLower(SHA256.HashData(PluginBytes)))]);

    private static VibeHomeManifest Manifest => Shipped with { Versions = [Entry] };

    private void Write(string relative, string text) => WriteBytes(relative, Encoding.UTF8.GetBytes(text));

    private void WriteBytes(string relative, byte[] bytes)
    {
        var path = _home.At(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>What Vibe 2.26.1 left in an empty home after a cold start and one turn (§24), with this test's pinned file.</summary>
    private void TheInventoryVibeWrites()
    {
        Write("vibehistory", "Hello Vibe!\r\n");
        Write("trusted_folders.toml", "trusted = []\nuntrusted = []\n");
        Write("connector_bootstrap_cache.json", "{}");
        Write(".connector_bootstrap_cache.json.lock", "x");
        Write("experiment_eval_cache.json", "{}");
        Write("whoami_cache.json", "{}");
        Write("logs/vibe.log", string.Empty);
        Write("logs/session/active/.registry", "x");
        Write("logs/session/unified/4f94d59a-afc4-fe1d-2354-ff6c5e3f799d/journal/0000000000000001.jsonl", "{}");
        Write("logs/session/unified/4f94d59a-afc4-fe1d-2354-ff6c5e3f799d/CURRENT", "1");
        WriteBytes(PluginPath, PluginBytes);
    }

    private string Refusal() => VibeHome.Refusal(_home, Manifest, Entry);

    private const string DataUuid = "193149ad-d8e5-069e-8f34-aece24b2623d";

    private const string SessionId = "4f94d59a-afc4-fe1d-2354-ff6c5e3f799d";

    /// <summary>
    /// What turn 1 of a conversation adds to §24's inventory (workc4-inv-after-turn1.json, vibe 2.26.1, 77 entries): the
    /// session store's chunks, generations and journals — all under `unified/*/**` — and the two EMPTY folders the manifest
    /// never saw, a plugin's per-session data folder and the plugin store's staging folder (PLAN_vibe_live_round_fixes.md §1).
    /// </summary>
    private void TheExtrasTurnOneLeaves()
    {
        var unified = $"logs/session/unified/{SessionId}/";
        Write(unified + "chunks/" + new string('a', 64) + ".json", "{}");
        Write(unified + "chunks/" + new string('b', 64) + ".json", "{}");
        Write(unified + "meta.json", "{}");
        Write(unified + "scheduled-loops.json", "[]");
        Write(unified + "journal/0000000000000022.jsonl", "{}");
        foreach (var generation in new[] { "0000000000000001", "0000000000000021" })
        {
            foreach (var name in new[] { "checkpoint", "manifest", "projection-state", "runtime-state" })
            {
                Write($"{unified}generations/{generation}/{name}.json", "{}");
            }
        }

        Directory.CreateDirectory(_home.At("logs", "session", "plugins", "data", DataUuid, "vibe"));
        Directory.CreateDirectory(_home.At("logs", "session", "plugins", "tmp"));
    }

    [Fact]
    public void TheHomeATurnOneLeaves_IsAdmittedByTheHashCheck_SoTheSecondTurnStarts()
    {
        TheInventoryVibeWrites();
        TheExtrasTurnOneLeaves();

        Refusal().Should().BeEmpty("the two empty folders are what Vibe 2.26.1 itself made (plugins/data/<session>/vibe and plugins/tmp)");
    }

    [Fact]
    public void TheHomeATurnOneLeaves_IsAdmittedByTheShapeCheckToo()
    {
        TheInventoryVibeWrites();
        TheExtrasTurnOneLeaves();

        VibeHome.Refusal(_home, Manifest).Should().BeEmpty();
    }

    /// <summary>A plugin's `${PLUGIN_DATA}` can hold code Vibe adopts without re-checking it (_materialize.py:220-245): folders only.</summary>
    [Theory]
    [InlineData("state.json", "{}")]
    [InlineData("hooks/hooks.json", "{}")]
    [InlineData(".mcp.json", """{"mcpServers":{"x":{"command":"y"}}}""")]
    public void AFileInAPluginsPerSessionData_IsRefusedOnBothChecks(string file, string text)
    {
        TheInventoryVibeWrites();
        TheExtrasTurnOneLeaves();
        var relative = $"logs/session/plugins/data/{DataUuid}/vibe/{file}";
        Write(relative, text);

        Refusal().Should().Contain($"holds {relative},").And.Contain("plugin's per-session data");
        VibeHome.Refusal(_home, Manifest).Should().Contain($"holds {relative},").And.Contain("plugin's per-session data");
    }

    /// <summary>The glob is `data/**`: a file directly in `plugins/data/` or in a session's folder is as much plugin data as one deeper (it was admitted by place under `data/*/**`).</summary>
    [Theory]
    [InlineData("stray.json")]
    [InlineData("193149ad-d8e5-069e-8f34-aece24b2623d.json")]
    public void AFileDirectlyUnderThePluginDataFolder_IsRefusedOnBothChecks(string file)
    {
        TheInventoryVibeWrites();
        TheExtrasTurnOneLeaves();
        var relative = $"logs/session/plugins/data/{file}";
        Write(relative, "{}");

        Refusal().Should().Contain($"holds {relative},").And.Contain("plugin's per-session data");
        VibeHome.Refusal(_home, Manifest).Should().Contain($"holds {relative},").And.Contain("plugin's per-session data");
    }

    [Fact]
    public void AFileVibeStagesWhileUnpacking_IsAdmittedByName_ButAStarterInItIsStillRefused()
    {
        TheInventoryVibeWrites();
        TheExtrasTurnOneLeaves();
        const string Staged = "logs/session/plugins/tmp/package-0123456789abcdef-0123456789abcdef/";
        Write(Staged + "x.partial", "half a file");
        Refusal().Should().BeEmpty("a reviewer killed mid-unpack leaves files in the staging folder; its retry must not be refused over them");
        VibeHome.Refusal(_home, Manifest).Should().BeEmpty();

        Write(Staged + "hooks.json", "{}");
        Refusal().Should().Contain($"holds {Staged}hooks.json,").And.Contain("hook");
        VibeHome.Refusal(_home, Manifest).Should().Contain($"holds {Staged}hooks.json,").And.Contain("hook");
    }

    private static string[] ListIn(string text, string name)
    {
        var at = text.IndexOf($"const {name} = [", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"scripts/vibe-home-manifest.mjs must declare `const {name} = [ … ];`");
        var close = text.IndexOf("];", at, StringComparison.Ordinal);

        var code = string.Join('\n', text[at..close].Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        return [.. System.Text.RegularExpressions.Regex.Matches(code, "'([^']+)'").Select(match => match.Groups[1].Value)];
    }

    [Fact]
    public void TheShippedManifest_NamesTheStagingFolderAndTheDataFolders_AsTheScriptDoes()
    {
        // reads-another-program: scripts/vibe-home-manifest.mjs generates the JSON; its lists are a copy of the C# side's rules.
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root(), "scripts", "vibe-home-manifest.mjs"));
        var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "vibe-home-manifest.json"))).RootElement;
        var varying = manifest.GetProperty("varying").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var folders = manifest.TryGetProperty("varyingFolders", out var list) ? list.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];

        varying.Should().Contain("logs/session/plugins/tmp/**");
        folders.Should().Equal("logs/session/plugins/data/**");
        varying.Should().Equal(ListIn(script, "VARYING"), "the JSON's varying is the script's constant verbatim");
        folders.Should().Equal(ListIn(script, "VARYING_FOLDERS"), "and so is varyingFolders");
        manifest.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("version").GetString() == "2.26.1")
            .GetProperty("deepestChild").GetInt32().Should().Be(156, "134 persisted + 22 for Vibe's atomic-write wrapper (RESULTS §27)");
    }

    [Fact]
    public void AnEmptyHome_IsAdmitted()
    {
        Refusal().Should().BeEmpty();
        VibeHome.Refusal(_home.At("not-made-yet"), Manifest, Entry).Should().BeEmpty("a home not made yet is an empty one");
    }

    [Fact]
    public void WhatVibeWritesIntoItsHome_IsAdmitted()
    {
        TheInventoryVibeWrites();

        Refusal().Should().BeEmpty("§24's inventory is exactly what an owned home is measured to gain");
    }

    /// <param name="named">What the refusal names: the file, or the first folder on its way that Vibe never writes.</param>
    [Theory]
    [InlineData("hooks.toml", "[[hooks]]\nname = \"x\"\ntype = \"post_agent\"\ncommand = \"python -c 1\"\n", "hooks.toml")]
    [InlineData("config.toml", "[[mcp_servers]]\nname = \"x\"\ntransport = \"stdio\"\ncommand = \"python\"\n", "config.toml")]
    [InlineData("config.toml", "theme = \"dark\"\n", "config.toml")]
    [InlineData("agents/plan.toml", "bypass_tool_permissions = true\n", "agents/")]
    [InlineData("tools/probe_tool.py", "open('x', 'w')\n", "tools/")]
    [InlineData(".env", "MISTRAL_API_KEY=x\n", ".env")]
    [InlineData("logs/session/unified/loose-file", "a file where Vibe keeps only session folders\n", "logs/session/unified/loose-file")]
    public void AFileVibeDoesNotWrite_RefusesTheLaunch_NamingThePath(string relative, string text, string named)
    {
        TheInventoryVibeWrites();
        Write(relative, text);

        Refusal().Should().Contain($"holds {named},").And.Contain(_home.Path).And.Contain("delete");
    }

    [Theory]
    [InlineData("agents")]
    [InlineData("tools")]
    [InlineData("skills")]
    public void AnEmptyFolderVibeDoesNotWrite_RefusesTheLaunch(string folder)
    {
        TheInventoryVibeWrites();
        Directory.CreateDirectory(_home.At(folder));

        Refusal().Should().Contain(folder + "/");
    }

    [Theory]
    [InlineData("trusted = [\n    \"C:\\\\Users\\\\x\\\\work\",\n]\nuntrusted = []\n")]
    [InlineData("trusted = [\"C:\\\\x\"]\nuntrusted = []\n")]
    [InlineData("trusted = []\nuntrusted = [\"C:\\\\x\"]\n")]
    public void ATrustListThatTrustsAFolder_RefusesTheLaunch(string trust)
    {
        TheInventoryVibeWrites();
        Write("trusted_folders.toml", trust);

        Refusal().Should().Contain("trusted_folders.toml").And.Contain("trust");
    }

    [Fact]
    public void APluginFileWithOtherBytes_RefusesTheLaunch()
    {
        TheInventoryVibeWrites();
        Write(PluginPath, "tampered");

        Refusal().Should().Contain(PluginPath).And.Contain("bytes");
    }

    [Fact]
    public void APluginFileTheManifestDoesNotName_RefusesTheLaunch()
    {
        TheInventoryVibeWrites();
        Write("logs/session/plugins/blobs/aa/another-blob", "{}");

        Refusal().Should().Contain("holds logs/session/plugins/blobs/aa/another-blob,").And.Contain("9.9.9");
    }

    [Fact]
    public void APluginFolderTheManifestDoesNotName_RefusesTheLaunch()
    {
        TheInventoryVibeWrites();
        Write("logs/session/plugins/packages/zz/plugin.json", "{}");

        Refusal().Should().Contain("holds logs/session/plugins/packages/,");
    }

    [Fact]
    public void TheShapeCheck_AdmitsAPluginFileByItsPlace_ButStillRefusesAHooksFile()
    {
        // Asked before the installed version is known (before `vibe --version` starts on the home): no hash yet.
        TheInventoryVibeWrites();
        Write(PluginPath, "bytes no manifest names");
        VibeHome.Refusal(_home, Manifest).Should().BeEmpty("the shape check admits a file under the pinned prefix by its place");

        Write("hooks.toml", "[[hooks]]\n");
        VibeHome.Refusal(_home, Manifest).Should().Contain("hooks.toml");
    }

    /// <summary>A cached plugin package root, as Vibe's store lays it out (<c>packages/&lt;ab&gt;/&lt;digest&gt;/</c>).</summary>
    private const string Package = "logs/session/plugins/packages/zz/zz-digest/";

    private const string DeclaresAServer =
        """{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"canary":{"type":"stdio","command":"python"}}}""";

    /// <summary>
    /// The risk consultation of 2026-10-13: Vibe 2.26.1 checks a cached package out WITHOUT re-digesting it
    /// (<c>_store.py:265-266</c>) and connects the servers its root declares (<c>_native.py:1661</c> for <c>mcp.json</c>,
    /// the Claude/Codex adapters for <c>.mcp.json</c> and their manifests' <c>mcpServers</c>) before any tool filter — so the
    /// shape check, all that guards an unmeasured version, refuses a package file that would start one.
    /// </summary>
    [Theory]
    [InlineData("mcp.json", DeclaresAServer)]
    [InlineData("MCP.json", DeclaresAServer)]
    [InlineData(".mcp.json", """{"mcpServers":{"canary":{"command":"python"}}}""")]
    [InlineData(".claude-plugin/plugin.json", """{"name":"p","mcpServers":"./servers.json"}""")]
    [InlineData(".claude-plugin/plugin.json", """{"name":"p","lspServers":{"x":{"command":"python"}}}""")]
    [InlineData(".codex-plugin/plugin.json", """{"name":"p","hooks":{"SessionStart":[]}}""")]
    [InlineData("kimi.plugin.json", """{"name":"p","sessionStart":{"command":"python"}}""")]
    [InlineData(".kimi-plugin/plugin.json", """{"name":"p","mcpServers":{"x":{}}}""")]
    [InlineData("mcp.json", "not json at all")]
    public void TheShapeCheck_RefusesACachedPluginPackage_ThatWouldStartAServer(string file, string text)
    {
        TheInventoryVibeWrites();
        Write(Package + file, text);

        VibeHome.Refusal(_home, Manifest).Should().Contain($"holds {Package}{file},").And.Contain("start");
    }

    [Theory]
    [InlineData("ai.mistral.vibe/hooks.toml")]
    [InlineData("hooks/hooks.json")]
    [InlineData("hooks.json")]
    public void TheShapeCheck_RefusesAHookFileInACachedPluginPackage(string file)
    {
        TheInventoryVibeWrites();
        Write(Package + file, "[[hooks]]\n");

        VibeHome.Refusal(_home, Manifest).Should().Contain($"holds {Package}{file},").And.Contain("hook");
    }

    [Theory]
    [InlineData("mcp.json", """{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{}}""")]
    [InlineData(".claude-plugin/plugin.json", """{"name":"p","mcpServers":null,"hooks":[]}""")]
    [InlineData("plugin.json", """{"name":"p"}""")]
    [InlineData("skills/vibe/SKILL.md", "mcpServers: a word in a skill, not a declaration\n")]
    public void TheShapeCheck_AdmitsAPackageFile_ThatStartsNothing(string file, string text)
    {
        TheInventoryVibeWrites();
        Write(Package + file, text);

        VibeHome.Refusal(_home, Manifest).Should().BeEmpty("a package file that declares no server and no hook is admitted by its place");
    }

    [Fact]
    public void TheShapeCheck_AdmitsADeclaringFile_ThatAMeasuredVersionPins_WithThoseBytes()
    {
        TheInventoryVibeWrites();
        var pinned = Manifest with
        {
            Versions = [Entry with { Pinned = [.. Entry.Pinned, new VibePinnedFile(Package + "mcp.json", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DeclaresAServer))))] }],
        };
        Write(Package + "mcp.json", DeclaresAServer);
        VibeHome.Refusal(_home, pinned).Should().BeEmpty("a measured Vibe unpacks exactly this file with exactly these bytes");

        Write(Package + "mcp.json", DeclaresAServer.Replace("python", "pwsh", StringComparison.Ordinal));
        VibeHome.Refusal(_home, pinned).Should().Contain($"holds {Package}mcp.json,", "the pin admits those bytes only");
    }

    [Fact]
    public void ALinkInTheHome_RefusesTheLaunch()
    {
        TheInventoryVibeWrites();
        using var outside = TempDir.For("coai-vibe-outside-");
        SnapshotTestKit.LinkNow(_home.At("logs", "session", "unified", "linked"), outside);

        Refusal().Should().Contain("linked").And.Contain("link");
    }

    [Fact]
    public void AHomeThatIsItselfALink_IsRefused()
    {
        using var target = TempDir.For("coai-vibe-target-");
        var link = _home.At("home-link");
        SnapshotTestKit.LinkNow(link, target);

        VibeHome.Refusal(link, Manifest, Entry).Should().Contain(link).And.Contain("link");
    }

    [Theory]
    [InlineData("logs/session/unified/*/**", "logs/session/unified/abc/journal/1.jsonl", true)]
    [InlineData("logs/session/unified/*/**", "logs/session/unified/abc/CURRENT", true)]
    [InlineData("logs/session/unified/*/**", "logs/session/unified/abc", false)]
    [InlineData("logs/vibe.log", "logs/vibe.log", true)]
    [InlineData("logs/vibe.log", "logs/vibe.log/x", false)]
    [InlineData("vibehistory", "VIBEHISTORY", false)]
    public void AVaryingName_MatchesItsGlob_AndNothingElse(string glob, string path, bool matches)
    {
        VibeHome.Matches(glob.Split('/'), path.Split('/')).Should().Be(matches);
    }
}

/// <summary>
/// <c>shared/vibe-home-manifest.json</c> is embedded and read — the one generated by <c>scripts/vibe-home-manifest.mjs</c>
/// for Vibe 2.26.1 (research/RESULTS_vibe_confinement.md §24) — and a document missing what the check needs is refused,
/// never read as a manifest that admits nothing.
/// </summary>
public sealed class VibeHomeManifestTests
{
    [Fact]
    public void TheShippedManifest_QualifiesVibe2261_WithItsTwelvePluginFiles()
    {
        var manifest = VibeHomeManifest.Builtin.Should().BeOfType<VibeManifestLoad.Loaded>().Subject.Manifest;

        manifest.PinnedPrefix.Should().Be("logs/session/plugins/");
        var entry = manifest.For("2.26.1").Should().ContainSingle().Subject;
        entry.Pinned.Should().HaveCount(12).And.OnlyContain(file => file.Path.StartsWith(manifest.PinnedPrefix, StringComparison.Ordinal) && file.Sha256.Length == 64);
        manifest.Varying.Should().Contain([".connector_bootstrap_cache.json.lock", "trusted_folders.toml", "logs/session/unified/*/**"]);
        manifest.For("2.27.0").Should().BeEmpty("a version nobody ran the script for is not qualified");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"pinnedPrefix":"logs/session/plugins/","varying":["vibehistory"],"versions":[]}""")]
    [InlineData("""{"pinnedPrefix":"logs/session/plugins/","varying":["vibehistory"],"versions":[{"version":"2.26.1","pinned":[{"path":"a","sha256":"short"}]}]}""")]
    [InlineData("not json")]
    public void AManifestMissingWhatTheCheckNeeds_IsRefused(string json)
    {
        VibeHomeManifest.Parse(json).Should().BeOfType<VibeManifestLoad.Broken>()
            .Which.Reason.Should().Contain("vibe-home-manifest.json");
    }
}
