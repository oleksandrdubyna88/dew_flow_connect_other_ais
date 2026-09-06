using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer is told what it actually has — and never told a repository is there when it is not.
/// </summary>
/// <remarks>
/// <para>Measured 2026-09-06: of 71 antigravity reviewer runs, 15 — 21 % — returned nothing at all,
/// every one of them at the same wall. The CLI's own log said it wanted permission to run a command
/// and, running headless, had nobody to ask; so it refused itself and produced no answer, which the
/// panel could only report as an empty reviewer.</para>
/// <para>It went looking because the prompt told it to. Eighteen of the twenty-five shipped prompts
/// opened with "You have the checkout read-only and the diff below" — while the DEFAULT code mode
/// launches a reviewer in an empty temp directory with no checkout in it. The claim was false for
/// most runs of the product, and it was false in a file that cannot know which mode is running.</para>
/// <para>So the sentence moved to the one place that does know. These tests hold both halves: the
/// prompt files stay silent about what is on disk, and the composer says the true thing for the mode
/// it is composing for.</para>
/// </remarks>
public sealed class ThePromptSaysWhatTheReviewerHasTests
{
    [Fact]
    public void NoShippedPrompt_ClaimsACheckoutItCannotKnowIsThere()
    {
        var prompts = new RolePrompts(Path.GetTempPath());
        var claiming = PromptCatalog.All.Where(c => Claims(prompts.ForChoice(c))).Select(c => c.Id).ToList();

        // The guard on the guard: a typo in the catalog would make the query above pass by finding
        // nothing at all, which is how a test of an emptied collection stays green for ever.
        PromptCatalog.All.Should().HaveCountGreaterThan(20, "the catalog is what is being searched");
        claiming.Should().BeEmpty(
            "a prompt file cannot know whether a checkout was mounted — only PanelService does");
    }

    [Fact]
    public void TheScanItself_StillFindsTheClaimWhenItIsThere()
    {
        // The companion the project's own rule demands for a structural scan: a prohibition that
        // would pass over EMPTY text proves nothing, and the catalog above is loaded through a call
        // that could start returning empty strings without failing anything else. So this asserts
        // the two things that would make the scan vacuous — that the search matches the sentence it
        // forbids, and that every prompt it searched is real text.
        Claims("You have the checkout read-only and the diff below.").Should().BeTrue(
            "the search used by the prohibition must actually match the sentence it forbids");

        var prompts = new RolePrompts(Path.GetTempPath());
        foreach (var choice in PromptCatalog.All)
        {
            prompts.ForChoice(choice).Length.Should().BeGreaterThan(200,
                $"{choice.Id} must be real text, or the scan over it means nothing");
        }
    }

    /// <summary>The one search, used by the prohibition and by its own control.</summary>
    private static bool Claims(string text) => text.Contains("checkout", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void InFastMode_TheReviewerIsToldThereIsNothingToLookAt()
    {
        var said = PanelService.WhatYouHave(hasCheckout: false);

        said.Should().Contain("no checkout");
        said.Should().Contain("no tool you can call",
            "the 21 % that returned nothing were models trying to run a command they could not");
        said.Should().NotContain("READ-ONLY checkout");
    }

    [Fact]
    public void WithAWorktreeMounted_TheReviewerIsToldTheCheckoutIsThere()
    {
        var said = PanelService.WhatYouHave(hasCheckout: true);

        said.Should().Contain("READ-ONLY checkout");
        said.Should().NotContain("no tool you can call");
    }

    [Fact]
    public void TheTwoModesDoNotSayTheSameThing()
    {
        PanelService.WhatYouHave(true).Should().NotBe(PanelService.WhatYouHave(false));
    }

    [Fact]
    public void EveryComposedPrompt_CarriesTheSentence_AndTheModeReachesIt()
    {
        // Source-read rather than composed, because ComposePrompt hangs off a live panel. The two
        // assertions are the whole fix: the sentence is interpolated into every prompt the composer
        // builds, and the call site passes the REAL mode rather than a constant. A `true` wired in
        // here would pass every other test in this file and ship the original defect.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "PanelService.cs"));

        source.Should().Contain("{WhatYouHave(hasCheckout)}",
            "every composed prompt carries it, including one a person overrode in the catalog");
        source.Should().Contain("ComposePrompt(choice, context, hasCheckout)",
            "the real mode reaches the prompt — a literal here would pass every other test in this file");
        source.Should().Contain("var hasCheckout = !fastCode && !isPlan",
            "a plan round stands in an empty scratch directory, so it has no checkout either");
    }

    [Fact]
    public void TheRepairAttempt_CoversTheAnswerThatWasNeverWritten()
    {
        // The repair prompt is built before anybody knows how the first attempt failed, and it used
        // to describe exactly one failure: "YOUR PREVIOUS ANSWER WAS NOT VALID JSON". A model that
        // returned NOTHING because a tool was refused was then told its JSON was malformed — advice
        // about a failure it did not have, which is why the retries died the same way as the runs.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "PanelService.cs"));

        source.Should().Contain("DID NOT PRODUCE A USABLE ANSWER");
        source.Should().Contain("refused", "the second failure shape has to be named to be answered");
    }

    /// <summary>
    /// The strip removes the product's own sentence and nothing that resembles it.
    /// </summary>
    /// <remarks>
    /// Every case here was named by a reviewer at the gate round on the change that introduced the
    /// strip: unanchored it ate a person's own words, and with \s* in front it ate a paragraph
    /// separator. The last case is the one that matters most — a prompt somebody wrote is theirs.
    /// </remarks>
    [Theory]
    // The two forms that actually shipped, including the line wrap the prompt files were written with.
    [InlineData(
        "You are a reviewer. You have the checkout read-only and the diff below. Review the change.",
        "You are a reviewer. Review the change.")]
    [InlineData(
        "You are a reviewer. You have the\nrepository checkout read-only and the diff below.\nReview the change.",
        "You are a reviewer. Review the change.")]
    // Standing alone on its own line, indented, between two paragraphs: the separators survive.
    [InlineData(
        "Opening guidance.\n\n  You have the checkout read-only and the diff below.\n\nReturn JSON.",
        "Opening guidance.\n\n\nReturn JSON.")]
    // Inside somebody's OWN sentence it is not the product's claim, and must not be touched.
    [InlineData(
        "I know you have the checkout read-only and the diff below. Ignore that.",
        "I know you have the checkout read-only and the diff below. Ignore that.")]
    // Quoted, as a person explaining the old wording to a reviewer.
    [InlineData(
        "Do not say \"You have the checkout read-only and the diff below.\" to me.",
        "Do not say \"You have the checkout read-only and the diff below.\" to me.")]
    public void TheStripTakesTheProductsSentence_AndLeavesEverythingElse(string given, string expected)
    {
        PanelService.WithoutTheStaleClaim(given).Should().Be(expected);
    }

    [Fact]
    public void APromptThatIsNothingBUTTheClaim_IsNotEmptied()
    {
        // An empty prompt produces the silent empty answer this whole change exists to stop, so a
        // strip that would leave nothing keeps the original and lets the composed sentence disagree
        // with it. Blank is the one outcome worse than contradictory.
        const string onlyTheClaim = "You have the checkout read-only and the diff below.";

        PanelService.WithoutTheStaleClaim(onlyTheClaim).Should().Be(onlyTheClaim);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src_vs_code")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found from the test binary");
    }
}
