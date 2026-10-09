using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A CLI row's own timeout replaces the round's reviewer timeout for that row (research/PLAN_one_model_catalog.md, epic 2,
/// story 2): a slow model on one row no longer needs every reviewer of the round to wait as long.
/// </summary>
/// <remarks>
/// The same rule as the extension's (<c>isMinutes</c>): a whole number from 1 to 1440, never on an api row — which keeps
/// its whole-review <c>reviewMinutes</c> — and anything else is unset, the round's timeout.
/// </remarks>
public sealed class ARowsOwnTimeoutTests
{
    [Theory]
    [InlineData("15", 15)]
    [InlineData("1440", 1440)]
    [InlineData("0", 0)]
    [InlineData("-3", 0)]
    [InlineData("1441", 0)]
    public void TheRowsMinutesAreParsed_OutOfRangeIsUnset(string minutes, int expected)
    {
        PanelSettings.ParseVendors($$"""[{"id":"claude","runtime":"claude","timeoutMinutes":{{minutes}}}]""")[0]
            .TimeoutMinutes.Should().Be(expected);
    }

    [Fact]
    public void ACliRowsMinutes_AreItsLaunchTimeout()
    {
        var row = new ProviderSettings("claude") { Runtime = "claude", TimeoutMinutes = 2 };

        RosterBuilder.TimeoutFor(row, new ClaudeRuntime(), TimeSpan.FromMinutes(10)).Should().Be(TimeSpan.FromMinutes(2));
        RosterBuilder.TimeoutFor(row with { TimeoutMinutes = 0 }, new ClaudeRuntime(), TimeSpan.FromMinutes(10))
            .Should().Be(TimeSpan.FromMinutes(10), "a row that sets none waits as long as the round says");
    }

    [Fact]
    public void AnApiRow_KeepsTheRoundsLaunchTimeout_ItsOwnLimitIsTheWholeReview()
    {
        var row = new ProviderSettings("qwen") { Runtime = "api", BaseUrl = "https://q.example/v1", TimeoutMinutes = 2 };

        RosterBuilder.TimeoutFor(row, new ApiRuntime("qwen", "https://q.example/v1"), TimeSpan.FromMinutes(10))
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void TheRoundsLaunch_IsGivenTheRowsTimeout()
    {
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-row-timeout-{Guid.NewGuid():N}"),
                ReviewerTimeout = TimeSpan.FromMinutes(10),
                Providers = [new ProviderSettings("codex") { Enabled = true, Runtime = "codex", TimeoutMinutes = 3 }],
            },
            VaultKeys.None("no vault"), default, new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);

        var work = service.Roster.BuildWork([RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false, codexTiers: CodexTiers.None).Reviewers;

        work.Should().ContainSingle().Which.Invocation.Request.Timeout.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheBinaryListsTheField()
    {
        FeaturesMode.Listed.Should().Contain("timeoutMinutes");
    }
}
