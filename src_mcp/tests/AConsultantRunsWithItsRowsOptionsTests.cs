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

    private static ReviewerSettings SettingsOf(ProviderSettings row) =>
        ConsultantTurnInputs.Settings(row, row.Model, Callers, "D:/data", VaultKeys.None("t"), Core.Api.ApiOverrides.None);

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
