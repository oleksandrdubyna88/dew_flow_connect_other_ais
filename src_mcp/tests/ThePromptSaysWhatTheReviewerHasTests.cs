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
        var claiming = RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts).Where(c => Claims(prompts.ForChoice(c))).Select(c => c.Id).ToList();

        // The guard on the guard: a typo in the catalog would make the query above pass by finding
        // nothing at all, which is how a test of an emptied collection stays green for ever.
        RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts).Should().HaveCountGreaterThan(20, "the catalog is what is being searched");
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
        foreach (var choice in RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts))
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
        var said = ReviewerPrompt.WhatYouHave(ReaderMaterial.Change);

        said.Should().Contain("no checkout");
        said.Should().Contain("no tool you can call",
            "the 21 % that returned nothing were models trying to run a command they could not");
        said.Should().NotContain("READ-ONLY checkout");
    }

    [Fact]
    public void WithAWorktreeMounted_TheReviewerIsToldTheCheckoutIsThere()
    {
        var said = ReviewerPrompt.WhatYouHave(ReaderMaterial.Checkout);

        said.Should().Contain("READ-ONLY checkout");
        said.Should().NotContain("no tool you can call");
    }

    [Fact]
    public void TheThreeMaterialsDoNotSayTheSameThing()
    {
        Enum.GetValues<ReaderMaterial>().Select(ReviewerPrompt.WhatYouHave).Should().OnlyHaveUniqueItems(
            "each is a different truth about what the reviewer holds");
    }

    /// <summary>
    /// The feature reviewer's truth (S2.2b): an outline with the changed hunks, the marks it will meet, and
    /// source it may ask for — worded to be TRUE while the loop that answers a request does not exist yet.
    /// </summary>
    [Fact]
    public void AFeatureReviewer_IsToldItHasAnOutlineAndHunks_AndThatARequestIsRecordedNotServed()
    {
        var said = ReviewerPrompt.WhatYouHave(ReaderMaterial.Outline);

        said.Should().Contain("no checkout").And.Contain("no tool you can call", "a feature reviewer has no checkout either");
        said.Should().Contain("OUTLINE").And.Contain("CHANGED HUNKS", "the two halves of the pack are named");
        said.Should().Contain("`*`", "the changed-member marker the outline carries is explained");
        said.Should().Contain("[N more changed lines — ask for source]", "the truncation marker a capped hunk ends with is explained");
        said.Should().Contain("What this context left out", "and where the cut material is named");
        said.Should().Contain("`sourceRequests`", "the way to ask is named");
        said.Should().Contain("RECORDED").And.Contain("NOT answered inside this review",
            "the loop that serves a request is S3.2 — promising source would make a reviewer hold its findings back");
        said.Should().NotContain("READ-ONLY checkout");
    }

    /// <summary>The marker the prompt quotes is the one the hunks really end with — read from the code that writes it.</summary>
    [Fact]
    public void TheTruncationMarkerThePromptQuotes_IsTheOneAHunkEndsWith()
    {
        var entries = System.Collections.Immutable.ImmutableArray.Create(
            new Core.Outlining.OutlineEntry(0, "method", "Big", "void Big()", 1, 2000));
        var file = new Core.Feature.OutlinedFile(
            new Core.Feature.ChangedFile("src/Big.cs", string.Empty, Core.Feature.FileChange.Modified, 1500, 0, false),
            Core.Outlining.SourceOutline.Of(Core.Outlining.OutlineLanguage.CSharp, 1000, entries),
            [new Core.Feature.LineSpan(2, 1501)],
            [.. Enumerable.Range(0, 1500).Select(i => new Core.Feature.DiffLine(0, '+', 2 + i, $"+    var line{i} = {i};"))]);

        var unit = Core.Feature.MemberHunks.Units(file).Single();

        unit.Truncated.Should().BeTrue("the fixture must be longer than the per-member cap");
        var marker = System.Text.RegularExpressions.Regex.Match(unit.Text, @"\[\d+ more changed lines — ask for source\]").Value;
        marker.Should().NotBeEmpty();
        ReviewerPrompt.WhatYouHave(ReaderMaterial.Outline).Should().Contain(
            System.Text.RegularExpressions.Regex.Replace(marker, @"\d+", "N"));
    }

    [Fact]
    public void EveryComposedPrompt_CarriesTheSentence_AndTheModeReachesIt()
    {
        // Source-read rather than composed, because ComposePrompt hangs off a live panel. The two
        // assertions are the whole fix: the sentence is interpolated into every prompt the composer
        // builds, and the call site passes the REAL mode rather than a constant. A `true` wired in
        // here would pass every other test in this file and ship the original defect.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "PanelService.cs"))
            + File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "Rounds", "RosterBuilder.cs"))
            + File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "Rounds", "ReviewerPrompt.cs"));

        source.Should().Contain("{WhatYouHave(material)}",
            "every composed prompt carries it, including one a person overrode in the catalog");
        source.Should().Contain("ComposePrompt(choice, context, material, stageRow.Answers)",
            "the real mode reaches the prompt — a literal here would pass every other test in this file");
        source.Should().Contain("var material = hasCheckout ? ReaderMaterial.Checkout : stageRow.Reads",
            "and what a reviewer without a checkout holds is the STAGE's answer, not a constant");
        source.Should().Contain("var hasCheckout = readsCheckout && !fastCode",
            "the mode is TOLD, and a round that reads no checkout has none however it is configured");
        source.Should().NotContain("!isPlan",
            "it was derived from the stage until plan 4, which made `isPlanStage: true` the only way "
            + "to ask for a document round's workspace — a claim about identity made to get a "
            + "behaviour, and two reviewers said so");
    }

    [Fact]
    public void TheRepairAttempt_CoversTheAnswerThatWasNeverWritten()
    {
        // The repair prompt is built before anybody knows how the first attempt failed, and it used
        // to describe exactly one failure: "YOUR PREVIOUS ANSWER WAS NOT VALID JSON". A model that
        // returned NOTHING because a tool was refused was then told its JSON was malformed — advice
        // about a failure it did not have, which is why the retries died the same way as the runs.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "PanelService.cs"))
            + File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "Rounds", "RosterBuilder.cs"));

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
        ReviewerPrompt.WithoutTheStaleClaim(given).Should().Be(expected);
    }

    [Fact]
    public void APromptThatIsNothingBUTTheClaim_IsNotEmptied()
    {
        // An empty prompt produces the silent empty answer this whole change exists to stop, so a
        // strip that would leave nothing keeps the original and lets the composed sentence disagree
        // with it. Blank is the one outcome worse than contradictory.
        const string onlyTheClaim = "You have the checkout read-only and the diff below.";

        ReviewerPrompt.WithoutTheStaleClaim(onlyTheClaim).Should().Be(onlyTheClaim);
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
