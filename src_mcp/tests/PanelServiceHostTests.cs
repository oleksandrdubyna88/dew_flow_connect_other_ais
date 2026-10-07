using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Settings edited in the panel must reach the NEXT round, not the next restart.
/// </summary>
/// <remarks>
/// The gap was invisible from both ends: the panel saves instantly and says so, the server had
/// already read the file, and nothing anywhere reported a setting that had not applied. The
/// operator diagnosed it from the symptom alone — "maybe they only apply after a restart" — which
/// is exactly the kind of question a product should never make anyone ask.
/// </remarks>
public sealed class PanelServiceHostTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("coai-host-").FullName;
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private string SettingsPath => Path.Combine(_dataDir, SettingsFile.Name);

    private string? Env(string name) => name == "COAI_DATA_DIR" ? _dataDir : null;

    private PanelServiceHost NewHost() =>
        new(Env, VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), _log, Noticing.None);

    private void WriteVendors(string json) =>
        File.WriteAllText(SettingsPath, $$"""{"COAI_VENDORS": {{System.Text.Json.JsonSerializer.Serialize(json)}}}""");

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    [Fact]
    public void AVendorAddedInThePanel_ReachesTheNextCall_WithoutRestartingTheServer()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"","baseUrl":""}]""");
        var host = NewHost();
        host.Current.Should().NotBeNull();

        // The person adds gemini in the sidebar; the panel rewrites the file underneath us.
        Touch(() => WriteVendors(
            """[{"id":"codex","runtime":"codex","model":"","baseUrl":""},{"id":"gemini","runtime":"gemini","model":"","baseUrl":""}]"""));

        Providers(host).Should().BeEquivalentTo(["codex", "gemini"]);
    }

    [Fact]
    public void AModelChangedInThePanel_IsTheModelTheNextRoundUses()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var host = NewHost();

        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));

        host.Current.Settings.Providers.Single(p => p.Provider == "codex").Model.Should().Be("gpt-5.6-mini");
    }

    [Fact]
    public void AnUnchangedFile_HandsBackTheSameService_RatherThanRebuildingPerCall()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"","baseUrl":""}]""");
        var host = NewHost();

        host.Current.Should().BeSameAs(host.Current, "an unchanged file must not cost a rebuild on every tool call");
    }

    [Fact]
    public void AnEnvironmentVariable_StillOutranksTheFile_AfterAReload()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"","baseUrl":""}]""");
        var host = new PanelServiceHost(
            name => name switch
            {
                "COAI_DATA_DIR" => _dataDir,
                "COAI_VENDORS" => """[{"id":"claude","runtime":"claude","model":"haiku","baseUrl":""}]""",
                _ => null,
            },
            VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), _log, Noticing.None);

        Touch(() => WriteVendors("""[{"id":"gemini","runtime":"gemini","model":"","baseUrl":""}]"""));

        Providers(host).Should().BeEquivalentTo(
            ["claude"], "a variable in the client's config is more specific than a file any window may rewrite");
    }

    private static IEnumerable<string> Providers(PanelServiceHost host) =>
        host.Current.Settings.Providers.Select(p => p.Provider);

    /// <summary>
    /// Writes, then forces a distinguishable timestamp.
    /// </summary>
    /// <remarks>
    /// Two writes inside one filesystem timestamp tick are indistinguishable by design (the stamp
    /// is deliberately not a content hash — it runs on every call). A test that wrote twice in the
    /// same millisecond would be measuring the clock, not the reload.
    /// </remarks>
    /// <summary>
    /// The cadence consultation (codex, 2026-10-07): the host recorded the new settings stamp BEFORE the rebuild, so a
    /// rebuild that threw left the old service serving under the new stamp, and every later call kept the old settings
    /// without trying again. The stamp and the service are published together, after a successful build.
    /// </summary>
    [Fact]
    public void AReloadThatFails_IsTriedAgainOnTheNextCall_RatherThanHiddenBehindTheNewStamp()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var host = NewHost();
        var sessions = Directory.CreateDirectory(Path.Combine(_dataDir, "sessions")).FullName;
        var bad = Path.Combine(sessions, "session-bad.json");
        File.WriteAllText(bad, """{"state":null,"rounds":[]}"""); // a session the startup sweep cannot survive

        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));
        host.Invoking(h => h.Current).Should().Throw<Exception>("the rebuild met the bad session");

        File.Delete(bad);
        host.Current.Settings.Providers.Single(p => p.Provider == "codex").Model.Should().Be("gpt-5.6-mini",
            "the failed reload is tried again, so the panel's change still arrives");
    }

    private void Touch(Action write)
    {
        write();
        File.SetLastWriteTimeUtc(SettingsPath, DateTime.UtcNow.AddSeconds(1));
    }
}
