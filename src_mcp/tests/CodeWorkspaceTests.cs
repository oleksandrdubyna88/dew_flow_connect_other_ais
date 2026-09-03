using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whether a code reviewer is handed the checkout, or only what the server composed for it.
/// </summary>
/// <remarks>
/// <para><b>Why this exists: the hosted and local reviewers were never given the same input.</b> A
/// hosted CLI is agentic — given a worktree it explores it, and the measurements show what that
/// costs: about 200 000 input tokens for one code round against roughly 25 000 for a local
/// reviewer, which is handed one composed prompt and has nowhere to go. Comparing the two is
/// therefore comparing two different questions, and the difference in their answers cannot be
/// attributed to the models.</para>
///
/// <para><c>COAI_CODE_WORKSPACE=none</c> launches the code reviewers in an empty directory. The
/// PROMPT is unchanged — the diff is assembled from the repository and the project's written rules
/// are still read from the worktree, both by the server — so what changes is only whether the
/// reviewer can go looking for more.</para>
///
/// <para>The mechanism is not new. The repair launch has always been given an empty directory for
/// exactly this reason, recorded beside it: "an agentic CLI handed a checkout goes exploring
/// instead". The plan stage does the same. This makes the code stage able to.</para>
/// </remarks>
public class CodeWorkspaceTests
{
    [Fact]
    public void TheCheckoutIsTheDefault_BecauseExploringIsUsuallyWorthIt()
    {
        PanelSettings.FromEnvironment(_ => null).CodeWorkspace.Should().Be("worktree");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("  none  ")]
    public void ItCanBeTurnedOff(string value)
    {
        PanelSettings.FromEnvironment(n => n == "COAI_CODE_WORKSPACE" ? value : null)
            .CodeWorkspace.Should().Be("none");
    }

    [Fact]
    public void AValueThisBuildDoesNotKnowKeepsTheCheckoutAndSaysSo()
    {
        // The same rule as every other setting here: fall back to the conservative behaviour, and
        // be audible about it rather than quietly doing something else.
        var settings = PanelSettings.FromEnvironment(n => n == "COAI_CODE_WORKSPACE" ? "sandbox" : null);

        settings.CodeWorkspace.Should().Be("worktree");
        settings.Unrecognised.Should().ContainSingle()
            .Which.Should().Contain("COAI_CODE_WORKSPACE").And.Contain("sandbox");
    }

    [Fact]
    public void AnEmptyValueIsNotAComplaint()
    {
        PanelSettings.FromEnvironment(n => n == "COAI_CODE_WORKSPACE" ? "" : null)
            .Unrecognised.Should().BeEmpty();
    }

    private static PanelService Service(string workspace)
    {
        var settings = new PanelSettings
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"coai-ws-{Guid.NewGuid():N}"),
            CodeWorkspace = workspace,
            Providers = [new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" }],
        };
        return new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);
    }

    private static string Worktree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coai-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The half the settings tests cannot see — and could not: removing the wiring entirely left
    /// all six of them green, which is the third time in one day a test sat one layer below the
    /// defect it was written for. This one reads the directory a reviewer is ACTUALLY given.
    /// </summary>
    [Fact]
    public void WithNone_ACodeReviewerIsLaunchedSomewhereOtherThanTheCheckout()
    {
        var worktree = Worktree();

        var work = Service("none").BuildWork([ReviewRole.Architecture], worktree, "ctx", round: 1);

        work.Should().NotBeEmpty();
        work[0].Invocation.Request.WorkingDirectory.Should().NotBe(worktree,
            "`none` exists so an agentic CLI has nothing to explore");
        Directory.Exists(work[0].Invocation.Request.WorkingDirectory).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(work[0].Invocation.Request.WorkingDirectory)
            .Should().BeEmpty("an empty directory, not another copy of the tree");
    }

    [Fact]
    public void WithWorktree_ACodeReviewerGetsTheCheckout()
    {
        var worktree = Worktree();

        var work = Service("worktree").BuildWork([ReviewRole.Architecture], worktree, "ctx", round: 1);

        work[0].Invocation.Request.WorkingDirectory.Should().Be(worktree);
    }

    [Fact]
    public void APlanRoundIsUnaffected_ItNeverHadACheckoutToGiveUp()
    {
        // A plan round already runs in a scratch directory, and its work is built with planPrompts.
        // The setting must not reach into it and hand it a second, different empty directory.
        var scratch = Worktree();

        var work = Service("none").BuildWork([ReviewRole.PlanCritique], scratch, "ctx", round: 1,
            planPrompts: ["plan-critique"]);

        work[0].Invocation.Request.WorkingDirectory.Should().Be(scratch);
    }
}
