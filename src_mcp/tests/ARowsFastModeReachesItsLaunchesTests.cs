using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A row's <c>fast</c> crosses from the settings file to every launch of the row (todo/PLAN_fast_mode.md, Story A):
/// parsed by the reviewer row's parser, absent or unknown read as Off (the owner's default), carried by the reviewer
/// roster, the consultation and the question row alike — and listed in <c>--features</c> as <c>fastMode</c>, so the
/// extension sends it only to a binary that reads it.
/// </summary>
public sealed class ARowsFastModeReachesItsLaunchesTests
{
    private static FastMode Parsed(string field) =>
        PanelSettings.ParseVendors("[{\"id\":\"codex\",\"runtime\":\"codex\"" + field + "}]")[0].Fast;

    [Theory]
    [InlineData(",\"fast\":\"on\"", FastMode.On)]
    [InlineData(",\"fast\":\"cli\"", FastMode.Cli)]
    [InlineData(",\"fast\":\"off\"", FastMode.Off)]
    [InlineData(",\"fast\":\" On \"", FastMode.On)]
    [InlineData("", FastMode.Off)]
    [InlineData(",\"fast\":\"turbo\"", FastMode.Off)]
    public void TheRowsFast_IsParsed_AbsentOrUnknownIsOff(string field, FastMode expected)
    {
        Parsed(field).Should().Be(expected);
    }

    [Fact]
    public void TheReviewerRoster_CarriesIt()
    {
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-fastroster-{Guid.NewGuid():N}"),
                Providers = [new("codex") { Enabled = true, Runtime = "codex", Fast = FastMode.On }],
            },
            VaultKeys.None("no vault"), default, new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);

        var work = service.Roster.BuildWork([RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false).Reviewers;

        work[0].Invocation.Request.Arguments.Should().ContainInConsecutiveOrder("-c", "service_tier=fast");
    }

    [Fact]
    public void AConsultation_CarriesIt()
    {
        var row = new ProviderSettings("codex") { Runtime = "codex", Fast = FastMode.Cli };

        ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), "D:/data", VaultKeys.None("t"), Core.Api.ApiOverrides.None)
            .Fast.Should().Be(FastMode.Cli);
    }

    [Fact]
    public void TheBinaryListsFastMode()
    {
        FeaturesMode.Listed.Should().Contain("fastMode");
    }
}
