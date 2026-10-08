using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A codex that refuses the standard tier is never told it (research/PLAN_codex_tier_floor.md, change 2): codex 0.110.0–0.130.0
/// fail the whole launch on <c>-c service_tier=default</c> (research/RESULTS_codex_service_tier_versions_2026-10-07.md), so
/// an Off row on one of them is sent NO tier — it has no way to say "standard". On still sends <c>fast</c>, which every
/// release measured accepts; "As the CLI is set" still sends nothing; and a codex nobody asked, one that could not tell,
/// and one that accepts are all told exactly what they were told before the probe existed.
/// </summary>
/// <remarks>
/// The pure half: what <c>CodexRuntime.TierArgs</c> makes of <see cref="ReviewerSettings.CodexTier"/>, through every
/// codex argv builder. That each launch path actually FILLS it is proven per site against a real fake codex, in the tests
/// beside each site's own fixtures.
/// </remarks>
public sealed class ACodexThatRefusesTheStandardTierTests
{
    private const string Repo = "D:/rsd/some-checkout";

    private static CodexTierSupport Tier(StandardTier answer) => answer switch
    {
        StandardTier.Unprobed => CodexTierSupport.Unprobed,
        _ => new CodexTierSupport(answer, "set by the test"),
    };

    private static IEnumerable<string> Tiers(IReadOnlyList<string> args) =>
        args.Zip(args.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal)).Select(pair => pair.Second);

    private static ReviewerSettings Settings(FastMode fast, StandardTier answer) =>
        new("codex") { Fast = fast, CodexTier = Tier(answer) };

    private static IReadOnlyList<string> Reviewer(FastMode fast, StandardTier answer) =>
        new CodexRuntime().Build(RoleCatalog.ArchitectureRole, "review this", Repo, "D:/s.json", "D:/out", Settings(fast, answer)).Request.Arguments;

    private static IReadOnlyList<string> Consultant(FastMode fast, StandardTier answer, string handle = "") =>
        new CodexConsultant(new CodexRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", handle, "D:/answers", Settings(fast, answer)))
            .Request.Arguments;

    [Theory]
    [InlineData(FastMode.Off, StandardTier.RefusesStandard, new string[0])]
    [InlineData(FastMode.Off, StandardTier.Unprobed, new[] { "service_tier=default" })]
    [InlineData(FastMode.Off, StandardTier.Unknown, new[] { "service_tier=default" })]
    [InlineData(FastMode.Off, StandardTier.Accepts, new[] { "service_tier=default" })]
    [InlineData(FastMode.On, StandardTier.RefusesStandard, new[] { "service_tier=fast" })]
    [InlineData(FastMode.On, StandardTier.Accepts, new[] { "service_tier=fast" })]
    [InlineData(FastMode.Cli, StandardTier.RefusesStandard, new string[0])]
    [InlineData(FastMode.Cli, StandardTier.Accepts, new string[0])]
    public void EveryCodexArgv_IsToldTheTierItsReleaseCanTake(FastMode fast, StandardTier answer, string[] sent)
    {
        var because = $"{fast} on a codex that {answer}";

        Tiers(Reviewer(fast, answer)).Should().Equal(sent, because);
        Tiers(Consultant(fast, answer)).Should().Equal(sent, because);
        Tiers(Consultant(fast, answer, handle: "0198f2c1-thread")).Should().Equal(sent, because);
    }

    [Fact]
    public void NobodyAsked_IsItsOwnState_AndTheDefault()
    {
        new ReviewerSettings("codex").CodexTier.Should().Be(CodexTierSupport.Unprobed,
            "a launch no site probed is told what it was told before the probe existed");
    }

    // ---------- providers: the card does not claim a state the launch did not send ----------

    /// <summary>
    /// <c>providers</c> reports the fast mode a row REQUESTS — and for an Off codex row on a release that refuses the
    /// standard tier, says beside it that the release cannot be told it, so its launches carry no tier (plan change 4).
    /// Read off the version the health probe already asked for: one <c>--version</c>, not two.
    /// </summary>
    [Theory]
    [InlineData("codex-cli 0.120.0\n", "codex", "off", true)]
    [InlineData("codex-cli 0.160.0\n", "codex", "off", false)]
    [InlineData("codex-cli 0.120.0\n", "fastcodex", "on", false)]
    [InlineData("codex-cli 0.160.0\n", "fastcodex", "on", false)]
    public async Task Providers_SaysWhenAnOffRowsReleaseCannotBeToldTheStandardTier(string printed, string row, string fast, bool said)
    {
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-tierreport-{Guid.NewGuid():N}"),
                Providers = PanelSettings.ParseVendors("""[{"id":"codex","runtime":"codex"},{"id":"fastcodex","runtime":"codex","fast":"on"}]"""),
            },
            VaultKeys.None("no vault"), default, new RecordingLauncher(stdOut: printed), Serilog.Core.Logger.None, Noticing.None);

        var answer = System.Text.Json.JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;
        var status = answer.GetProperty("providers").EnumerateArray().Single(one => one.GetProperty("provider").GetString() == row);

        status.GetProperty("fast").GetString().Should().Be(fast, "the requested state, as before");
        status.GetProperty("note").GetString()!.Contains("cannot be told the standard tier", StringComparison.Ordinal)
            .Should().Be(said, $"{row} on `{printed.Trim()}`: {status.GetProperty("note").GetString()}");
    }
}
