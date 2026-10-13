using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultant's row options reach its LAUNCH, not only its settings (the cadence consultation for epics 1–3 of
/// research/PLAN_one_model_catalog.md, finding C2, step C2b): the row's CLI effort, by the reviewers' own rule
/// (<c>RosterBuilder.EffortFor</c>), and the row's own timeout where its runtime takes one.
/// </summary>
public sealed class AConsultantRunsWithItsRowsOptionsTests
{
    private static readonly TimeSpan Callers = TimeSpan.FromMinutes(5);

    private static ReviewerSettings SettingsOf(ProviderSettings row, PanelSettings? panel = null) =>
        ConsultantTurnInputs.Settings(row, row.Model, Callers, panel ?? new PanelSettings { DataDir = "D:/data" }, VaultKeys.None("t"));

    /// <summary>
    /// A local consultant with no effort of its own is told the PANEL's local effort and ceiling, as a local reviewer is
    /// (todo/PLAN_local_effort_reaches_every_local_launch.md, S1.1) — read off the shim argv the turn is launched with.
    /// NON-default values, so a launch hard-coding <c>none</c>/8192 could not pass.
    /// </summary>
    [Fact]
    public void ALocalConsultant_IsToldThePanelsLocalEffortAndCeiling()
    {
        var panel = new PanelSettings { DataDir = "D:/data", LocalReasoningEffort = "medium", LocalMaxTokens = 4096 };
        var row = new ProviderSettings("gemma") { Runtime = "local", Model = "gemma4", BaseUrl = "http://127.0.0.1:9/v1" };
        // The local shim's build WRITES its prompt file into the answers directory, so the directory is the test's own:
        // a fixed "D:/answers" failed the win-arm64 release leg of mcp 0.52.1 (no D: drive there) and left prompt files
        // on every machine that had one.
        var answers = Directory.CreateTempSubdirectory("coai-localconsultant-").FullName;
        try
        {
            var arguments = ConsultantResolution.For(row.Identity())!
                .Build(new ConsultantLaunch("D:/rsd/some-checkout", "help me", string.Empty, answers, SettingsOf(row, panel), Path.Combine(answers, "schema.json")))
                .Request.Arguments;

            arguments.Should().ContainInConsecutiveOrder(["--reasoning-effort", "medium"]);
            arguments.Should().ContainInConsecutiveOrder(["--max-tokens", "4096"]);
        }
        finally
        {
            Directory.Delete(answers, recursive: true);
        }

        Directory.Exists(answers).Should().BeFalse("the test leaves no prompt file behind on any machine");
    }

    [Theory]
    [InlineData("claude", "high", "high")]
    [InlineData("local", "low", "low")]
    // A codex row's effort is kept and not sent while codex is unmeasured — the reviewers' rule, unchanged.
    [InlineData("codex", "high", "")]
    public void TheRowsCliEffort_ReachesTheLaunchByTheReviewersRule(string runtime, string effort, string sent)
    {
        SettingsOf(new ProviderSettings(runtime) { Runtime = runtime, CliEffort = effort }).ReasoningEffort.Should().Be(sent);
    }

    [Fact]
    public void AClaudeConsultant_IsToldItsEffort()
    {
        var settings = SettingsOf(new ProviderSettings("claude") { Runtime = "claude", CliEffort = "high" });

        var arguments = new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch("D:/rsd/some-checkout", "help me", string.Empty, "D:/answers", settings with { ClaudeCli = ClaudeCapability.WithRestricted }))
            .Request.Arguments;

        arguments.Should().ContainInConsecutiveOrder("--effort", "high");
    }

    [Fact]
    public void AClaudeConsultantWithNoEffort_IsToldNone()
    {
        var arguments = new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch("D:/rsd/some-checkout", "help me", string.Empty, "D:/answers", SettingsOf(new ProviderSettings("claude") { Runtime = "claude" }) with { ClaudeCli = ClaudeCapability.WithRestricted }))
            .Request.Arguments;

        arguments.Should().NotContain("--effort");
    }

    [Fact]
    public void TheRowsOwnTimeout_BoundsItsTurn_WhereItsRuntimeTakesOne()
    {
        ConsultantTurnInputs.TurnTimeout(new ProviderSettings("claude") { Runtime = "claude", TimeoutMinutes = 9 }, Callers)
            .Should().Be(TimeSpan.FromMinutes(9));
        ConsultantTurnInputs.TurnTimeout(new ProviderSettings("claude") { Runtime = "claude" }, Callers)
            .Should().Be(Callers, "a row with no timeout of its own keeps the caller's");
        // An api row's own limit is the whole review (reviewMinutes), as for a reviewer.
        ConsultantTurnInputs.TurnTimeout(new ProviderSettings("glm") { Runtime = "api", BaseUrl = "https://glm.example", TimeoutMinutes = 9 }, Callers)
            .Should().Be(Callers);
        SettingsOf(new ProviderSettings("claude") { Runtime = "claude", TimeoutMinutes = 9 }).Timeout.Should().Be(TimeSpan.FromMinutes(9));
    }
}
