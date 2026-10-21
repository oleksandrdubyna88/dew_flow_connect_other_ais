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

    private PanelServiceHost NewHost(CancellationToken hostStopping = default) =>
        new(Env, VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), _log, Noticing.None, hostStopping);

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
            VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), _log, Noticing.None, CancellationToken.None);

        Touch(() => WriteVendors("""[{"id":"gemini","runtime":"gemini","model":"","baseUrl":""}]"""));

        Providers(host).Should().BeEquivalentTo(
            ["claude"], "a variable in the client's config is more specific than a file any window may rewrite");
    }

    /// <summary>
    /// The host's stopping token reaches EVERY service it builds (E3.S2 of todo/PLAN_the_round_counter_tail_E3.md, decision D4) — the first
    /// and the one rebuilt when the settings change — so a round's cancellation can be told a host stop from a withdrawal.
    /// </summary>
    [Fact]
    public void TheHost_HandsItsStopToEveryServiceItBuilds()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        using var stopping = new CancellationTokenSource();
        var host = NewHost(stopping.Token);
        var first = host.Current;

        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));
        var rebuilt = host.Current;

        rebuilt.Should().NotBeSameAs(first, "the settings changed, so a service was rebuilt");
        first.HostStopping.Should().Be(stopping.Token);
        rebuilt.HostStopping.Should().Be(stopping.Token, "a rebuild must not drop the host's token");
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
        // By design (gate G3, E3 S2): `Current` never throws to its callers, so the reload that met the bad session
        // answers with the last good settings. The retry on the NEXT call, which this test pins, is kept.
        CodexModel(host).Should().Be("gpt-5.6-terra", "the rebuild met the bad session, so the old settings stay");

        File.Delete(bad);
        CodexModel(host).Should().Be("gpt-5.6-mini",
            "the failed reload is tried again, so the panel's change still arrives");
    }

    /// <summary>
    /// E3 S2 (C14, gate G3): `Current` is read by twenty callers on every tool call and by the sweeper's beat, so a rebuild
    /// that throws must never reach them — it answers with the last good settings under the OLD stamp, and each later call
    /// tries again until the cause is gone.
    /// </summary>
    [Fact]
    public void CurrentNeverThrows_WhenTheRebuildThrows()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var host = NewHost();
        var sessions = Directory.CreateDirectory(Path.Combine(_dataDir, "sessions")).FullName;
        var bad = Path.Combine(sessions, "session-bad.json");
        File.WriteAllText(bad, """{"state":null,"rounds":[]}"""); // a session the startup sweep cannot survive
        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));

        var first = Record.Exception(() => host.Current);
        var second = Record.Exception(() => host.Current);

        first.Should().BeNull("a rebuild that throws is answered with the last good settings");
        second.Should().BeNull("and so is the retry on the next call, while the cause is still there");
        File.Delete(bad);
        CodexModel(host).Should().Be("gpt-5.6-mini", "the next call after the cause is gone shows the new file");
    }

    /// <summary>
    /// E3 S2 (C14): the settings file is held by its writer at the moment the server reloads it. The read used to turn
    /// that sharing violation into EMPTY settings and publish them under the new stamp, so the server ran on defaults
    /// until the file changed again. Busy keeps the last good settings and the old stamp; the next read after the release
    /// shows the new file.
    /// </summary>
    [Fact]
    public void ASettingsFileHeldOpenByItsWriter_KeepsTheLastGoodSettings()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the share-mode refusal is Windows'");
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var host = NewHost();
        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));

        string whileHeld;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            whileHeld = CodexModel(host);
        }

        whileHeld.Should().Be("gpt-5.6-terra", "a file its writer still holds is not a file with no settings in it");
        CodexModel(host).Should().Be("gpt-5.6-mini", "the stamp did not move, so the next call after the release reads the new file");
    }

    /// <summary>
    /// The busy path on EVERY OS, through the host's read seam (the arm above holds a real file and is Windows'): a read that fails with a sharing violation
    /// (HResult 0x80070020) keeps the last good settings, says so ONCE for that stamp however many calls ask, and sees the new file as soon as the read recovers.
    /// </summary>
    [Fact]
    public void ABusyRead_KeepsTheLastGoodSettings_SaysSoOncePerStamp_AndRecovers()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var busy = false;
        var events = new List<Serilog.Events.LogEvent>();
        var host = new PanelServiceHost(
            Env, VaultKeys.None("no vault in this test"), default, new ProcessLauncher(),
            new Serilog.LoggerConfiguration().WriteTo.Sink(new CapturedEvents(events)).CreateLogger(), Noticing.None,
            path => busy ? throw new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)) : File.ReadAllText(path));
        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));
        busy = true;

        var whileBusy = new[] { CodexModel(host), CodexModel(host), CodexModel(host) };

        whileBusy.Should().OnlyContain(model => model == "gpt-5.6-terra", "a file its writer holds is not a file with no settings in it");
        events.Count(e => e.Level == Serilog.Events.LogEventLevel.Warning && e.RenderMessage().Contains("held open by its writer")).Should().Be(1,
            "the retry is on every call, so the warning is one per stamp");
        busy = false;
        CodexModel(host).Should().Be("gpt-5.6-mini", "the stamp did not move, so the first call after the read recovers sees the new file");
    }

    /// <summary>
    /// E3 S2 (C14, decision 4 of the lead's decisions): a file locked down by mistake must never pin the last good settings
    /// for ever, so a permission denial is NOT busy — it keeps today's answer, the defaults. The denial comes through the
    /// host's internal read seam, not a real ACL (an ACL is Windows-only, needs cleanup that can fail, and tests the OS).
    /// </summary>
    [Fact]
    public void ADeniedSettingsFile_IsNotKeptAsTheLastGood()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var denied = false;
        var host = new PanelServiceHost(
            Env, VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), _log, Noticing.None,
            path => denied ? throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.") : File.ReadAllText(path));
        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));
        denied = true;

        CodexModel(host).Should().Be(DefaultCodexModel(), "a denial is not a holder: the person sees the defaults, not stale settings for ever");
    }

    /// <summary>
    /// Code gate G11: <c>Current</c> promises never to throw, and its stamp read runs OUTSIDE the reload's try. A file that goes away between the existence check and the
    /// length (a <see cref="FileNotFoundException"/>) was already an <see cref="IOException"/> the stamp keeps the old stamp for; a path the platform cannot stamp at all
    /// (here a data directory with a NUL in it, which throws <see cref="ArgumentException"/>) was not, and reached every caller of the twenty that read <c>Current</c>.
    /// </summary>
    [Fact]
    public void AStampThatCannotBeRead_KeepsTheLastGoodService_AndCurrentDoesNotThrow()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var broken = false;
        var host = new PanelServiceHost(
            name => name == "COAI_DATA_DIR" ? (broken ? _dataDir + "\u0000" : _dataDir) : null,
            VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), _log, Noticing.None, CancellationToken.None);
        var good = host.Current;
        broken = true;

        var act = () => host.Current;

        act.Should().NotThrow("the stamp is a courtesy for change detection; a read that fails is \"not changed\"");
        host.Current.Should().BeSameAs(good, "the last good service keeps serving under its old stamp");
    }

    /// <summary>Code judge J4 (the logging rule): a rebuild that failed is logged WITH the exception, as the exception, not as text inside the message.</summary>
    [Fact]
    public void AReloadThatFails_IsLoggedWithTheExceptionItself()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var events = new List<Serilog.Events.LogEvent>();
        var host = new PanelServiceHost(
            Env, VaultKeys.None("no vault in this test"), default, new ProcessLauncher(),
            new Serilog.LoggerConfiguration().WriteTo.Sink(new CapturedEvents(events)).CreateLogger(), Noticing.None, CancellationToken.None);
        var sessions = Directory.CreateDirectory(Path.Combine(_dataDir, "sessions")).FullName;
        File.WriteAllText(Path.Combine(sessions, "session-bad.json"), """{"state":null,"rounds":[]}"""); // a session the startup sweep cannot survive

        Touch(() => WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-mini","baseUrl":""}]"""));
        _ = host.Current;

        events.Should().ContainSingle(e => e.Level == Serilog.Events.LogEventLevel.Warning && e.RenderMessage().Contains("settings reload skipped"))
            .Which.Exception.Should().NotBeNull("the exception travels as the exception, so a sink can show its type and stack");
    }

    private sealed class CapturedEvents(List<Serilog.Events.LogEvent> into) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => into.Add(logEvent);
    }

    [Fact]
    public void AMalformedFileOnReload_GivesDefaults()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var host = NewHost();

        Touch(() => File.WriteAllText(SettingsPath, """{ "COAI_VENDORS": [ not json"""));

        CodexModel(host).Should().Be(DefaultCodexModel(), "a person who wrote a broken file sees the defaults and a warning, as before");
    }

    [Fact]
    public void AnEmptyFileOnReload_GivesDefaults()
    {
        WriteVendors("""[{"id":"codex","runtime":"codex","model":"gpt-5.6-terra","baseUrl":""}]""");
        var host = NewHost();

        Touch(() => File.WriteAllText(SettingsPath, string.Empty));

        CodexModel(host).Should().Be(DefaultCodexModel(), "an empty file is an explicit thing too: defaults, as before");
    }

    private static string CodexModel(PanelServiceHost host) =>
        host.Current.Settings.Providers.FirstOrDefault(p => p.Provider == "codex")?.Model ?? "(no codex row)";

    /// <summary>What a data directory with no settings file at all gives the codex row.</summary>
    private static string DefaultCodexModel()
    {
        using var empty = TempDir.For("testsToDelete-host-defaults-");

        return CodexModel(new PanelServiceHost(
            name => name == "COAI_DATA_DIR" ? empty.Path : null,
            VaultKeys.None("no vault in this test"), default, new ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None, CancellationToken.None));
    }

    private void Touch(Action write)
    {
        write();
        File.SetLastWriteTimeUtc(SettingsPath, DateTime.UtcNow.AddSeconds(1));
    }
}
