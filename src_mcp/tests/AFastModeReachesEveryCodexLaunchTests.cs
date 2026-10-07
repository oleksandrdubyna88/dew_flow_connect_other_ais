using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A codex row's fast mode reaches every codex launch (todo/PLAN_fast_mode.md, Story A): Off — the default, a row that
/// never set it included — sends <c>-c service_tier=default</c>, On sends <c>fast</c>, "As the CLI is set" sends
/// nothing. The spelling was measured first (research/RESULTS_fast_mode_measured_2026-10-07.md): codex reads the key,
/// checks it per model, and drops a value the model does not advertise with a warning — never a refusal.
/// </summary>
public sealed class AFastModeReachesEveryCodexLaunchTests
{
    private const string Repo = "D:/rsd/some-checkout";

    private static IEnumerable<string> Tiers(IReadOnlyList<string> args) =>
        args.Zip(args.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal)).Select(pair => pair.Second);

    private static IReadOnlyList<string> Reviewer(CodexRuntime runtime, FastMode fast) =>
        runtime.Build(RoleCatalog.ArchitectureRole, "review this", Repo, "D:/s.json", "D:/out", new ReviewerSettings("codex") { Fast = fast }).Request.Arguments;

    private static IReadOnlyList<string> Consultant(FastMode fast, string handle = "", LaunchConfinement? confinement = null) =>
        new CodexConsultant(new CodexRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", handle, "D:/answers", new ReviewerSettings("codex") { Fast = fast }) { Confinement = confinement ?? LaunchConfinement.AsShipped })
            .Request.Arguments;

    [Theory]
    [InlineData(FastMode.Off, "service_tier=default")]
    [InlineData(FastMode.On, "service_tier=fast")]
    public void AReviewerIsToldItsTier(FastMode fast, string sent)
    {
        Tiers(Reviewer(new CodexRuntime(), fast)).Should().Equal(sent);
    }

    [Fact]
    public void ARowThatNeverSetIt_IsOff()
    {
        new ReviewerSettings("codex").Fast.Should().Be(FastMode.Off, "the owner's default: it stops a hidden priority tier");
    }

    [Fact]
    public void AsTheCliIsSet_SendsNothing()
    {
        Tiers(Reviewer(new CodexRuntime(), FastMode.Cli)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(FastMode.Off)]
    [InlineData(FastMode.On)]
    public void ACodexOnSomebodyElsesEndpoint_IsToldNoTier(FastMode fast)
    {
        // A third-party model_provider has no codex service tier (the own critic of the plan round).
        Tiers(Reviewer(new CustomCodexRuntime("openrouter", "https://openrouter.example/api/v1"), fast)).Should().BeEmpty();
        Tiers(Reviewer(new DeepseekRuntime(), fast)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(FastMode.Off, "service_tier=default")]
    [InlineData(FastMode.On, "service_tier=fast")]
    public void EveryConsultantBranch_IsToldItsTier(FastMode fast, string sent)
    {
        Tiers(Consultant(fast)).Should().Equal(sent);
        Tiers(Consultant(fast, handle: "0198f2c1-thread")).Should().Equal(sent);
    }

    [Fact]
    public void AConsultantAsTheCliIsSet_IsToldNothing()
    {
        Tiers(Consultant(FastMode.Cli)).Should().BeEmpty();
        Tiers(Consultant(FastMode.Cli, handle: "0198f2c1-thread")).Should().BeEmpty();
    }
}
