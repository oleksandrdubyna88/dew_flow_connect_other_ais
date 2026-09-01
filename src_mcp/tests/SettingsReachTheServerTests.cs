using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The whole path a setting travels: the panel writes <c>settings.json</c> into the shared data
/// directory, and the NEXT tool call must be served with it — no client restart, no re-pasted
/// config block.
/// </summary>
/// <remarks>
/// <para>Tested end to end, in the file format the extension actually writes, because that is the
/// promise a person checks by changing a number and running a review. The two halves each had a
/// test of their own and the seam between them had none — which is exactly where "settings only
/// apply after a restart" lived unnoticed.</para>
/// <para>Every field is asserted individually. A round trip that silently drops one setting is
/// worse than one that drops all of them: nobody notices, and the review runs with a threshold or
/// a vendor nobody chose.</para>
/// </remarks>
public sealed class SettingsReachTheServerTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("coai-reach-").FullName;

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private string? Env(string name) => name == "COAI_DATA_DIR" ? _dataDir : null;

    private PanelServiceHost Host() =>
        new(Env, VaultKeys.None("no vault here"), default, new ProcessLauncher(), Serilog.Core.Logger.None);

    /// <summary>Writes what the extension's `serverSettingsJson` produces — env-shaped JSON.</summary>
    private void PanelWrites(params (string Key, string Value)[] settings)
    {
        var body = string.Join(",\n  ", settings.Select(s =>
            $"{System.Text.Json.JsonSerializer.Serialize(s.Key)}: {System.Text.Json.JsonSerializer.Serialize(s.Value)}"));
        File.WriteAllText(Path.Combine(_dataDir, SettingsFile.Name), $"{{\n  {body}\n}}");
        // Two writes inside one filesystem tick are indistinguishable by design; the panel cannot
        // produce them, but a test can.
        File.SetLastWriteTimeUtc(Path.Combine(_dataDir, SettingsFile.Name), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void EverySettingThePanelWrites_IsServingTheVeryNextCall()
    {
        var host = Host();
        host.Current.Should().NotBeNull("the first call establishes the baseline");

        PanelWrites(
            ("COAI_VENDORS", """[{"id":"antigravity","runtime":"antigravity","model":"gemini-3.7-flash-high","baseUrl":""}]"""),
            ("COAI_MAX_ROUNDS", "5"),
            ("COAI_GATE_THRESHOLD", "1"),
            ("COAI_ON_EXHAUSTED", "escalate"),
            ("COAI_MAX_CONCURRENCY", "7"),
            ("COAI_MAX_PER_PROVIDER", "4"),
            ("COAI_REVIEWER_TIMEOUT_MINUTES", "12"),
            ("COAI_ESCALATION_MINUTES", "45"),
            ("COAI_ROTATE_PROMPTS", "true"),
            ("COAI_PROMPTS_PER_ROUND", """{"SecurityReliability":["sec-attack","sec-memory-leaks"]}"""));

        var s = host.Current.Settings;

        s.Providers.Should().ContainSingle().Which.Provider.Should().Be("antigravity");
        s.Providers[0].Runtime.Should().Be("antigravity");
        s.Providers[0].Model.Should().Be("gemini-3.7-flash-high");
        s.Rounds.For(Stage.PlanReview).MaxRounds.Should().Be(5);
        s.Rounds.For(Stage.PlanReview).Threshold.Should().Be(1);
        s.Rounds.OnExhausted.Should().Be(StagePolicy.Escalate);
        s.GlobalConcurrency.Should().Be(7);
        s.PerProviderConcurrency.Should().Be(4);
        s.ReviewerTimeout.Should().Be(TimeSpan.FromMinutes(12));
        s.EscalationBudget.Should().Be(TimeSpan.FromMinutes(45));
        s.DealPlanLenses.Should().BeTrue("the legacy rotate flag still turns dealing on");
        s.PromptsPerRound["SecurityReliability"].Should().Equal("sec-attack", "sec-memory-leaks");
    }

    [Fact]
    public void APromptChosenInThePanel_IsThePromptTheNextRoundRuns()
    {
        // The setting is only real if it reaches the decision. This asserts the value AND what the
        // round does with it, which is the question a person is actually asking.
        var host = Host();
        host.Current.Should().NotBeNull();

        PanelWrites(("COAI_PROMPTS_PER_ROUND", """{"Architecture":["arch-evolution"]}"""));

        var settings = host.Current.Settings;
        PromptCatalog.ForRound(
            PromptCatalog.ArchitectureRole, 1,
            settings.PromptsPerRound.GetValueOrDefault(PromptCatalog.ArchitectureRole, []))
            .Id.Should().Be("arch-evolution");
    }

    [Fact]
    public void ChangingASettingBackToItsDefault_TakesEffectToo()
    {
        // The panel writes only what DIFFERS from the defaults, so returning a value to its
        // default removes the key entirely. If the server kept the last value it saw, a setting
        // could be turned on but never off.
        var host = Host();
        PanelWrites(("COAI_MAX_ROUNDS", "9"));
        host.Current.Settings.Rounds.For(Stage.PlanReview).MaxRounds.Should().Be(9);

        // The panel writes the file again without that key, which is what returning a control to its
        // default looks like from here.
        PanelWrites();
        host.Current.Settings.Rounds.For(Stage.PlanReview).MaxRounds.Should().Be(3, "a key that is gone is a value back at its default");
    }

    [Fact]
    public void TheClientsOwnEnvironment_StillOutranksThePanel()
    {
        // A variable in the MCP client's config is more specific than a file any window may
        // rewrite, and that order must not quietly invert when the file becomes live.
        var host = new PanelServiceHost(
            name => name switch
            {
                "COAI_DATA_DIR" => _dataDir,
                "COAI_MAX_ROUNDS" => "2",
                _ => null,
            },
            VaultKeys.None("none"), default, new ProcessLauncher(), Serilog.Core.Logger.None);

        PanelWrites(("COAI_MAX_ROUNDS", "9"));

        host.Current.Settings.Rounds.For(Stage.PlanReview).MaxRounds.Should().Be(2);
    }

    [Fact]
    public void AHalfWrittenSettingsFile_LeavesTheLastGoodConfigurationInPlace()
    {
        // The panel writes this file while a round may be starting. A torn read must not hand the
        // gate an empty vendor list — it would fail every reviewer and report a panel that agreed.
        var host = Host();
        PanelWrites(("COAI_VENDORS", """[{"id":"codex","runtime":"codex","model":"","baseUrl":""}]"""));
        host.Current.Settings.Providers.Should().ContainSingle();

        File.WriteAllText(Path.Combine(_dataDir, SettingsFile.Name), "{ \"COAI_VENDORS\": \"[{ half");
        File.SetLastWriteTimeUtc(Path.Combine(_dataDir, SettingsFile.Name), DateTime.UtcNow.AddSeconds(2));

        var act = () => host.Current.Settings.Providers;
        act.Should().NotThrow("a torn file is not a configuration");
        host.Current.Settings.Providers.Should().NotBeEmpty("a gate with no reviewers reviews nothing");
    }
}
