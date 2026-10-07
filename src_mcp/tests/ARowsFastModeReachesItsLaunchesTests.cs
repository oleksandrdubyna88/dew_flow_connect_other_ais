using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A row's <c>fast</c> crosses from the settings file to every launch of the row (research/PLAN_fast_mode.md, Story A):
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

        var work = service.Roster.BuildWork([RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false, codexTiers: CodexTiers.None).Reviewers;

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
    public async Task ProvidersReportsWhatEachRowWithATierRunsWith_AndNothingForOneWithout()
    {
        // What the seam reads back (research/PLAN_fast_mode.md, Story B): the binary's own word for the state it applies.
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-fastreport-{Guid.NewGuid():N}"),
                Providers = PanelSettings.ParseVendors(
                    """[{"id":"codex","runtime":"codex","fast":"on"},{"id":"claude","runtime":"claude","model":"opus"},{"id":"antigravity","runtime":"antigravity","fast":"on"},{"id":"or","runtime":"codex","baseUrl":"https://or.example/v1","fast":"on"}]"""),
            },
            VaultKeys.None("no vault"), default, new RecordingLauncher(stdOut: string.Empty), Serilog.Core.Logger.None, Noticing.None);

        var answer = System.Text.Json.JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;
        string Fast(string id) =>
            answer.GetProperty("providers").EnumerateArray().Single(one => one.GetProperty("provider").GetString() == id)
                is var row && row.TryGetProperty("fast", out var fast) ? fast.GetString() ?? "" : "(absent)";

        Fast("codex").Should().Be("on");
        Fast("claude").Should().Be("off", "an Opus row that never set it runs Off");
        Fast("antigravity").Should().Be("(absent)", "a runtime with no tier says nothing");
        Fast("or").Should().Be("(absent)", "a codex row on another endpoint has no codex tier");
    }

    [Fact]
    public void TheBinaryListsFastMode()
    {
        FeaturesMode.Listed.Should().Contain("fastMode");
    }
}
