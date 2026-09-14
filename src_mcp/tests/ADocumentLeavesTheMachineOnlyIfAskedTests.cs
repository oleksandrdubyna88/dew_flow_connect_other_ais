using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A document round reaches a Team server when somebody ticked the box, and not before.
/// </summary>
/// <remarks>
/// <para>Plan 4 confined a document to the repository the session was opened for and called that a
/// security boundary rather than a tidiness one — the tool reads a file and ships its contents to
/// other vendors' models, so an unrestricted path would have been an exfiltration primitive with a
/// friendly name. A Team server is the same question one step out: the text leaves this machine for a
/// shared box and runs under a company account rather than the person's own.</para>
/// <para><b>The switch that decided it was the PLAN tick.</b> Plan 4 chose that deliberately — a
/// person who set a vendor to read prose rather than diffs ticked that one — and for a local reviewer
/// it is a fair reading. It stops being fair the moment the same tick also decides whether a company
/// document crosses the network, which is what plan 5 makes possible. So an absent document switch
/// answers differently depending on where the reviewer runs, and these are the four cases.</para>
/// <para>The Blocking finding on plan 5's plan round was against the first draft of exactly this
/// rule, which wrote <c>Document ?? Plan</c> for every vendor and so would have granted the
/// permission retroactively, on every configuration written before documents existed.</para>
/// <para><b>Tested on <see cref="ProviderSettings.Serves"/> rather than through a round</b>, and the
/// reason is worth keeping: a remote vendor passes TWO gates, its own switch and the server's role
/// list, and a round is empty when either refuses. A test that built a round would be green with the
/// switch deleted, because the role gate would have emptied it anyway — the shape of an assertion
/// that survives its own break. The round-level case below is the local one, where this switch is
/// the only gate there is.</para>
/// </remarks>
public sealed class ADocumentLeavesTheMachineOnlyIfAskedTests
{
    private static ProviderSettings Local(bool? document = null, bool plan = true) =>
        new("local") { Enabled = true, Runtime = "local", Model = "qwen", Plan = plan, Document = document };

    private static ProviderSettings Remote(bool? document = null, bool plan = true) =>
        new("team-codex")
        {
            Enabled = true, Runtime = "remote", RemoteVendor = "codex", Model = "m",
            BaseUrl = "https://coai.example.com", Plan = plan, Document = document,
        };

    /// <summary>The migration reading, and it holds only where nothing leaves the machine.</summary>
    [Fact]
    public void ALocalVendorWithNoDocumentSwitch_FollowsItsPlanTick()
    {
        Local().Serves(Stage.DocumentReview).Should().BeTrue(
            "a settings file written before documents existed keeps the round it had — and nothing "
            + "about a local reviewer is a decision somebody needs to be asked for");
        Local(plan: false).Serves(Stage.DocumentReview).Should().BeFalse();
    }

    /// <summary>
    /// The consent case, and the one to revert-prove: put <c>Document ?? Plan</c> back and a company
    /// document goes to a Team server nobody ticked.
    /// </summary>
    [Fact]
    public void ATeamServerWithNoDocumentSwitch_DoesNotTakeDocuments()
    {
        Remote().Serves(Stage.DocumentReview).Should().BeFalse(
            "the plan tick meant 'this vendor reads prose', never 'this file may go to the shared box'");
        Remote().Serves(Stage.PlanReview).Should().BeTrue("its plan reviews are unaffected");
    }

    [Fact]
    public void ATeamServerTheOperatorTicked_DoesTakeThem()
    {
        Remote(document: true).Serves(Stage.DocumentReview).Should().BeTrue(
            "the whole point of the switch is that it can be turned on");
    }

    [Fact]
    public void AVendorTurnedOffForDocuments_TakesNoneEitherWay()
    {
        Local(document: false).Serves(Stage.DocumentReview).Should().BeFalse();
        Remote(document: false).Serves(Stage.DocumentReview).Should().BeFalse();
    }

    /// <summary>
    /// Documents and plans are separate decisions now, in both directions.
    /// </summary>
    /// <remarks>
    /// A person who wants documents from a vendor but no plan reviews gets exactly that; before this
    /// change the two could not be said apart at all.
    /// </remarks>
    [Fact]
    public void DocumentsAndPlansAreSeparateDecisions()
    {
        Local(document: true, plan: false).Serves(Stage.DocumentReview).Should().BeTrue();
        Local(document: true, plan: false).Serves(Stage.PlanReview).Should().BeFalse();
        Remote(document: true, plan: false).Serves(Stage.DocumentReview).Should().BeTrue();
    }

    /// <summary>The master switch still outranks all three.</summary>
    [Fact]
    public void ADisabledVendorTakesNothingHoweverItIsTicked()
    {
        var off = Local(document: true) with { Enabled = false };

        off.Serves(Stage.DocumentReview).Should().BeFalse();
    }

    /// <summary>
    /// A stage nobody wrote a switch for throws rather than quietly taking the code one.
    /// </summary>
    /// <remarks>
    /// The default arm of a three-way switch is where the next stage would land in silence, which is
    /// the failure this whole class exists because of: a document round rode the plan tick for one
    /// release because there was nowhere else for it to go and nothing said so.
    /// </remarks>
    [Fact]
    public void AStageWithNoSwitchIsRefusedRatherThanDefaulted()
    {
        var act = () => Local().Serves((Stage)999);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*add one rather than defaulting*");
    }

    /// <summary>And the whole way through a round, for the vendor where this switch is the only gate.</summary>
    [Fact]
    public void ALocalVendorTurnedOffForDocuments_IsNotLaunchedForADocumentRound()
    {
        using var data = TempDir.For("coai-consent-");
        using var scratch = TempDir.For("coai-consent-wt-");
        var service = new PanelService(
            new PanelSettings { DataDir = data, Providers = [Local(document: false)] },
            VaultKeys.None("no vault"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Serilog.Core.Logger.None);

        var work = service.BuildWork(
            [RoleCatalog.DocumentRole], scratch, "ctx", round: 1,
            stage: Stage.DocumentReview, readsCheckout: false);

        work.Reviewers.Should().BeEmpty();
    }

    /// <summary>The same vendor, ticked, does run one.</summary>
    [Fact]
    public void ALocalVendorTickedForDocuments_IsLaunchedForOne()
    {
        using var data = TempDir.For("coai-consent-");
        using var scratch = TempDir.For("coai-consent-wt-");
        var service = new PanelService(
            new PanelSettings { DataDir = data, Providers = [Local(document: true)] },
            VaultKeys.None("no vault"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Serilog.Core.Logger.None);

        var work = service.BuildWork(
            [RoleCatalog.DocumentRole], scratch, "ctx", round: 1,
            stage: Stage.DocumentReview, readsCheckout: false);

        work.Reviewers.Should().NotBeEmpty();
    }
}
