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
