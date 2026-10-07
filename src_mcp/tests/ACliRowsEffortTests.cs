using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A CLI row's effort means what <c>shared/feature-availability.json</c> says it means for its runtime
/// (todo/PLAN_one_model_catalog.md, epic 2, story 2 as revised: "Effort").
/// </summary>
/// <remarks>
/// <para>claude's levels were read off the installed CLI (2.1.289: <c>--effort &lt;level&gt;</c>, low medium high xhigh
/// max) and are passed as that flag; a level it does not list leaves that one reviewer out, by name. codex is
/// <c>unmeasured</c> — nobody has shown which values it takes — so a codex row's effort is kept and NOT sent, and the row
/// still reviews. A runtime that takes none (antigravity) with an effort set leaves that reviewer out. An older claude
/// without the flag refuses it itself; that failure is named for what it is.</para>
/// </remarks>
public sealed class ACliRowsEffortTests
{
    private static PanelService Service(params ProviderSettings[] providers) => new(
        new PanelSettings { DataDir = Path.Combine(Path.GetTempPath(), $"coai-clieffort-{Guid.NewGuid():N}"), Providers = providers },
        VaultKeys.None("no vault"), default, new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);

    private static IReadOnlyList<ReviewerWork> Work(PanelService service) =>
        service.Roster.BuildWork([RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false, codexTiers: CodexTiers.None).Reviewers;

    private static ProviderSettings Row(string runtime, string effort) =>
        new(runtime) { Enabled = true, Runtime = runtime, CliEffort = effort };

    [Fact]
    public void ACliRowsEffort_IsParsed_LowerCased()
    {
        PanelSettings.ParseVendors("""[{"id":"claude","runtime":"claude","effort":" High "}]""")[0].CliEffort.Should().Be("high");
    }

    [Fact]
    public void ClaudesLevel_IsPassedAsItsFlag_AndRecordedAsApplied()
    {
        var invocation = Work(Service(Row("claude", "high")))[0].Invocation;

        invocation.Request.Arguments.Should().ContainInConsecutiveOrder("--effort", "high");
        invocation.Effort.Should().Be("high", "the effort this launch actually applied is what the round records");
    }

    [Fact]
    public void ClaudeWithNoEffort_IsSentNoFlag()
    {
        Work(Service(Row("claude", string.Empty)))[0].Invocation.Request.Arguments.Should().NotContain("--effort");
    }

    [Fact]
    public void ACodexRowsEffort_IsKeptButNotSent_AndTheRowStillReviews()
    {
        var work = Work(Service(Row("codex", "high")));

        work.Should().ContainSingle("an E1-shaped settings file with a codex effort still yields a round with codex in it");
        // The flags themselves — a bare "effort" also matches this test's own temp directory name.
        work[0].Invocation.Request.Arguments.Should().NotContain("--effort");
        string.Join(' ', work[0].Invocation.Request.Arguments).Should().NotContain("model_reasoning_effort");
        FeatureAvailability.Builtin.EffortOf("codex").Source.Should().Be("unmeasured", "this is why; when it is measured, this test changes");
    }

    [Fact]
    public void AClaudeLevelItDoesNotList_LeavesThatReviewerOut_NamingTheLevels()
    {
        var service = Service(Row("claude", "turbo"), Row("codex", string.Empty));

        Work(service).Select(w => w.Invocation.Provider).Should().Equal("codex");
        service.ExcludedFrom(Stage.PlanReview).Should().ContainSingle()
            .Which.Should().Contain("claude").And.Contain("'turbo'").And.Contain(string.Join(", ", CoaiMcp.Core.Catalog.FeatureAvailability.Builtin.EffortOf("claude").Levels));
    }

    [Fact]
    public void AnEffortOnARuntimeThatTakesNone_LeavesThatReviewerOut()
    {
        var service = Service(Row("antigravity", "high"));

        Work(service).Should().BeEmpty();
        service.ExcludedFrom(Stage.PlanReview).Should().ContainSingle().Which.Should().Contain("takes no effort");
    }

    [Fact]
    public void AnOlderClaudeRefusingTheFlag_IsSaidForWhatItIs()
    {
        VendorDiagnosis.For("error: unknown option '--effort'\n").Should().Contain("does not take --effort");
    }

    [Fact]
    public void TheBinaryListsTheField()
    {
        FeaturesMode.Listed.Should().Contain("cliEffort");
    }
}
