using CoaiMcp.Core.Rounds;
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
    public void TheDiffAloneIsTheDefault_BecauseItWasMeasuredToFindMore()
    {
        // Not a preference. Three hosted models each found MORE useful defects without a checkout,
        // at a fraction of the tokens, and three real defects appeared that no run with one reached.
        PanelSettings.FromEnvironment(_ => null).CodeWorkspace.Should().Be("none");
    }

    [Theory]
    [InlineData("worktree")]
    [InlineData("WORKTREE")]
    [InlineData("  worktree  ")]
    public void TheCheckoutCanBeAskedFor(string value)
    {
        PanelSettings.FromEnvironment(n => n == "COAI_CODE_WORKSPACE" ? value : null)
            .CodeWorkspace.Should().Be("worktree");
    }

    [Fact]
    public void AValueThisBuildDoesNotKnowKeepsTheDefaultAndSaysSo()
    {
        var settings = PanelSettings.FromEnvironment(n => n == "COAI_CODE_WORKSPACE" ? "sandbox" : null);

        settings.CodeWorkspace.Should().Be("none");
        settings.Unrecognised.Should().ContainSingle()
            .Which.Should().Contain("COAI_CODE_WORKSPACE").And.Contain("sandbox");
    }

    [Fact]
    public void AnEmptyValueIsNotAComplaint()
    {
        PanelSettings.FromEnvironment(n => n == "COAI_CODE_WORKSPACE" ? "" : null)
            .Unrecognised.Should().BeEmpty();
    }

    private static PanelService Service(string workspace, string dataDir = "")
    {
        var settings = new PanelSettings
        {
            DataDir = dataDir.Length > 0 ? dataDir : Path.Combine(Path.GetTempPath(), $"coai-ws-{Guid.NewGuid():N}"),
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

        var work = Service("none").BuildWork([RoleCatalog.ArchitectureRole], worktree, "ctx", round: 1, isPlanStage: false);

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

        var work = Service("worktree").BuildWork([RoleCatalog.ArchitectureRole], worktree, "ctx", round: 1, isPlanStage: false);

        work[0].Invocation.Request.WorkingDirectory.Should().Be(worktree);
    }

    [Fact]
    public void TheRepairLaunch_IsToldItHasNoCheckout_EvenWhenTheReviewWasGivenOne()
    {
        // The repair launch has ALWAYS run in an empty directory, and the remark beside it says why:
        // it is not asking for a better review, it is asking for the answer in the schema, and an
        // agentic CLI handed a checkout goes exploring instead. Its PROMPT, though, was composed for
        // the review's mode — so in worktree mode it opened by telling the model it had a read-only
        // checkout and then told it there were no tools. One prompt, two contradictory sentences,
        // sent to the reviewer that had already failed once. Found by codex at this change's own
        // code round; its diagnosis was that the repair should promise the checkout, and reading the
        // code says the opposite: the repair never has one.
        var worktree = Worktree();

        var work = Service("worktree").BuildWork([RoleCatalog.ArchitectureRole], worktree, "ctx", round: 1, isPlanStage: false);

        Sent(work[0].Repair!).Should().Contain("no tool you can call",
            "the repair launch runs in an empty temp directory whatever the review got");
        Sent(work[0].Repair!).Should().NotContain("READ-ONLY checkout");
        Sent(work[0].Invocation).Should().Contain("READ-ONLY checkout",
            "the REVIEW launch really was given the tree, and must still be told so");
    }

    [Fact]
    public void APromptSomebodyOverrodeBeforeTheClaimWasRemoved_DoesNotArriveWithBothSentences()
    {
        // The catalog is editable, and an override written before 2026-09-06 still opens with "You
        // have the checkout read-only and the diff below." Composing the true sentence underneath it
        // hands the model two opposite instructions in one prompt — raised by gemini at the plan gate
        // and again at the code gate. Editing the shipped files cannot reach a person's own copy.
        var dataDir = Path.Combine(Path.GetTempPath(), $"coai-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dataDir, "prompts"));
        File.WriteAllText(
            Path.Combine(dataDir, "prompts", "architecture.md"),
            "You are an independent ARCHITECTURE reviewer of a change written by another AI. You have the\n"
            + "repository checkout read-only and the diff below. Review the change, not the whole codebase.\n");

        var work = Service("none", dataDir)
            .BuildWork([RoleCatalog.ArchitectureRole], Worktree(), "ctx", round: 1, isPlanStage: false);

        Sent(work[0].Invocation).Should().NotContain("checkout read-only",
            "a person's own copy of a prompt cannot know which mode is running either");
        Sent(work[0].Invocation).Should().Contain("no tool you can call");
        Sent(work[0].Invocation).Should().Contain("independent ARCHITECTURE reviewer",
            "only the false sentence is removed, never the person's prompt");
    }

    /// <summary>
    /// Everything the child is actually given, by whichever door this runtime uses: a vendor CLI
    /// takes the prompt on stdin (cmd.exe truncates an argument at its first newline), and the local
    /// engine takes a --prompt-file. Reading only stdin made both tests here fail against correct
    /// code, which is the cheapest possible reminder to assert on what is SENT, not on where.
    /// </summary>
    private static string Sent(ReviewerInvocation invocation)
    {
        var args = invocation.Request.Arguments;
        var at = args.ToList().IndexOf("--prompt-file");
        var file = at >= 0 && at + 1 < args.Count && File.Exists(args[at + 1])
            ? File.ReadAllText(args[at + 1])
            : string.Empty;
        return string.Join("\n", [invocation.Request.StdIn, file, string.Join(" ", args)]);
    }

    [Fact]
    public void APlanRoundIsUnaffected_ItNeverHadACheckoutToGiveUp()
    {
        // A plan round already runs in a scratch directory, and its work is built with planPrompts.
        // The setting must not reach into it and hand it a second, different empty directory.
        var scratch = Worktree();

        var work = Service("none").BuildWork([RoleCatalog.PlanRole], scratch, "ctx", round: 1, isPlanStage: true,
            planPrompts: ["plan-critique"]);

        work[0].Invocation.Request.WorkingDirectory.Should().Be(scratch);
    }
}
