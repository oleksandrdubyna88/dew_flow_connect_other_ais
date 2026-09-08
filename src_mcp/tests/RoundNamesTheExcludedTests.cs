using System.Collections.Immutable;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round that leaves out a reviewer somebody enabled says so, and says why.
/// </summary>
/// <remarks>
/// <para><b>The defect this is for, and it is the one that made three others invisible.</b> On
/// 2026-09-07 a Team-server reviewer was enabled, ticked for both stages, and never called. The log
/// held both halves of the contradiction eleven seconds apart:</para>
/// <code>
/// [11:03:41 INF] starting: codex,gemini,local,remsoftdev-claude enabled
/// [11:06:00 INF] round 1 PlanReview opening: 3 reviewer(s) — codex/…, gemini/…, local/…
/// </code>
/// <para>Four enabled, three asked, and nothing anywhere named the fourth. The round summary then
/// said <i>"all 3 reviewers answered"</i> — true about what it asked, and silent about what it did
/// not. A reviewer that IS asked and fails has always been reported honestly ("9 of 12 answered;
/// failed: …"), which is exactly why the silence about one never asked was so hard to see.</para>
/// </remarks>
public class RoundNamesTheExcludedTests
{
    private static ReviewerSummary Answered(int asked) => ReviewerSummary.AllAnswered(asked);

    [Fact]
    public void WithNobodyExcluded_TheSentenceIsWhatItHasAlwaysBeen()
    {
        // The guard on the other side. Every round that has nothing to add must read exactly as it
        // did, or this change is a change to every round rather than to the ones with a problem.
        Answered(3).Sentence.Should().Be("all 3 reviewers answered");
        new ReviewerSummary(3, 2, ["codex/Architecture: exit 1"]).Sentence
            .Should().Be("2 of 3 reviewers answered; failed: codex/Architecture: exit 1");
    }

    [Fact]
    public void AnEnabledReviewerThatCouldNotRunIsNamed_WithTheReason()
    {
        var summary = Answered(3) with
        {
            Excluded = ImmutableArray.Create(
                "remsoftdev-claude: not signed in to the Team server at https://coai.remsoft.dev"),
        };

        summary.Sentence.Should().StartWith("all 3 reviewers answered");
        summary.Sentence.Should().Contain("1 enabled reviewer could not run");
        summary.Sentence.Should().Contain("remsoftdev-claude");
        summary.Sentence.Should().Contain(
            "not signed in",
            "the count alone sends somebody hunting; the reason is what they act on");
    }

    [Fact]
    public void SeveralExcludedReviewersAreCounted_AndAllNamed()
    {
        var summary = Answered(1) with { Excluded = ImmutableArray.Create("a: no key", "b: not signed in") };

        summary.Sentence.Should().Contain("2 enabled reviewers could not run");
        summary.Sentence.Should().Contain("a: no key").And.Contain("b: not signed in");
    }

    private static PanelService With(params ProviderSettings[] providers) =>
        new(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-excl-{Guid.NewGuid():N}"),
                Providers = [.. providers],
            },
            VaultKeys.None("no vault"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Serilog.Core.Logger.None);

    /// <summary>A Team-server row on a machine that has not signed in: enabled, and unrunnable.</summary>
    private static ProviderSettings Remote(bool plan = true, bool code = true) =>
        new("remsoftdev-claude")
        {
            Enabled = true, Runtime = "remote", RemoteVendor = "claude", Model = "haiku",
            BaseUrl = "https://coai.example.com", Plan = plan, Code = code,
        };

    private static ProviderSettings Local() =>
        new("local") { Enabled = true, Runtime = "local", Model = "m" };

    [Fact]
    public void TheRoundKnowsWhoItLeftOut()
    {
        var excluded = With(Local(), Remote()).ExcludedFrom(isPlanStage: true);

        excluded.Should().ContainSingle().Which.Should()
            .StartWith("remsoftdev-claude: ").And.Contain("not signed in to its Team server");
    }

    /// <summary>
    /// The reason a MODEL reads is written on this side, never by the vendor.
    /// </summary>
    /// <remarks>
    /// A remote vendor's note can carry the SERVER's own text — <c>UnexpectedMessage</c> interpolates
    /// a response body — and this string travels into the round summary that reaches the calling AI.
    /// A remote service being able to put instruction-shaped text into an AI's instruction stream is
    /// a channel worth closing, and a review gate is precisely where somebody would want it. The full
    /// note is still what <c>providers</c> answers and what the panel shows: the surfaces a PERSON
    /// reads. Raised on epic 3's code round.
    /// </remarks>
    [Fact]
    public void TheReasonIsOursEvenWhenTheVendorsNoteIsNot()
    {
        var reason = With(Local(), Remote()).ExcludedFrom(isPlanStage: true).Single();

        // The note for this state names the server's ADDRESS and tells the person where to sign in.
        // None of that belongs in a sentence a model is handed as round status.
        reason.Should().NotContain("https://", "a URL out of a vendor's note is text this side did not write");
        reason.Should().NotContain("sign in from", "the cure is for the panel, not for the model");
        reason.Should().Be("remsoftdev-claude: this machine is not signed in to its Team server");
    }

    [Fact]
    public void NobodyIsBothAskedAndExcluded()
    {
        // The two lists are derived from ONE predicate, and this is what says so. Two predicates
        // that agree today is how the three copies of the runtime decision got away with it twice.
        var service = With(Local(), Remote());
        var worktree = Path.Combine(Path.GetTempPath(), $"coai-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(worktree);

        var asked = service.BuildWork([ReviewRole.PlanCritique], worktree, "ctx", round: 1, isPlanStage: true)
            .Select(w => w.Invocation.Provider).Distinct().ToList();
        var excluded = service.ExcludedFrom(isPlanStage: true).Select(e => e.Split(':')[0]).ToList();

        asked.Should().NotBeEmpty();
        asked.Should().NotIntersectWith(excluded);
    }

    [Fact]
    public void AReviewerTickedForCodeOnlyIsNotReportedAsExcludedFromAPlanRound()
    {
        // The stage filter runs BEFORE the availability one, in both directions. A person who turned
        // a vendor off for plans has not lost a reviewer; saying they did on every plan round would
        // train them to ignore the sentence, which is the one thing it cannot afford.
        With(Local(), Remote(plan: false)).ExcludedFrom(isPlanStage: true).Should().BeEmpty();
        With(Local(), Remote(plan: false)).ExcludedFrom(isPlanStage: false)
            .Should().ContainSingle("it IS enabled for code, and there it cannot run");
    }

    /// <summary>
    /// A reviewer that WAS asked and then failed is a failure, never an exclusion.
    /// </summary>
    /// <remarks>
    /// The two live in one sentence and a reader would expect them to blur, so this says they do
    /// not. Exclusion is decided BEFORE the roster, from <c>CanRun</c>; a timeout, a non-zero exit or
    /// a kill happens to a reviewer that entered it. They have different cures — one is a
    /// configuration, the other is a run — and a round that put a timed-out vendor in both lists
    /// would send somebody to fix a credential that is fine. Accepted finding, this epic's plan round.
    /// </remarks>
    [Fact]
    public void AReviewerThatWasAskedAndFailedIsAFailure_NotAnExclusion()
    {
        var summary = ReviewerSummaryFactory.From(
            [(new ReviewerInvocation("codex", ReviewRole.PlanCritique,
                new Runners.Processes.ProcessRequest("codex", [], "D:/wt")),
              new ReviewerOutcome.TimedOut())],
            excluded: ["remsoftdev-claude: not signed in"]);

        summary.Failures.Should().ContainSingle().Which.Should().Contain("codex/PlanCritique");
        summary.Excluded.Should().ContainSingle().Which.Should().StartWith("remsoftdev-claude");
        summary.Excluded.Should().NotContain(e => e.Contains("codex"), "codex was asked; it did not answer");
        summary.Sentence.Should().Contain("failed: codex").And.Contain("1 enabled reviewer could not run");
    }

    [Fact]
    public void ADisabledReviewerIsNotExcluded_ItIsOff()
    {
        var off = Remote() with { Enabled = false };

        With(Local(), off).ExcludedFrom(isPlanStage: true).Should().BeEmpty();
    }

    /// <summary>
    /// A round that ran out of TIME says so, and does not blame the reviewers it cut off.
    /// </summary>
    /// <remarks>
    /// <para>Raised on the round-deadline plan's own code round, before any of it was built, which
    /// is the cheapest moment to find a contract gap. Cancelling the outstanding reviewers makes
    /// each of them abandoned, and `Describe` has no case for that — it renders "unknown", which is
    /// the least useful word available for the one thing the feature exists to explain.</para>
    /// <para>So the deadline is passed in EXPLICITLY rather than inferred from a count of abandoned
    /// reviewers: inferring it would be wrong the moment a person cancels a round themselves.</para>
    /// </remarks>
    [Fact]
    public void ARoundThatRanOutOfTime_SaysThatRatherThanNamingItsVictims()
    {
        var summary = new ReviewerSummary(9, 6, ["codex/Architecture: cancelled"])
        {
            EndedByDeadline = TimeSpan.FromMinutes(40),
        };

        summary.Sentence.Should().Contain("6 of 9 reviewers answered");
        summary.Sentence.Should().Contain("the round reached its 40 minute limit",
            "the person needs to know the ROUND ran out, not that six reviewers mysteriously failed");
    }

    [Fact]
    public void ARoundWithinItsDeadline_SaysNothingAboutOne()
    {
        // The guard on the other side. Every round that finishes in time must read exactly as it
        // always did, or this is a change to every round in service of the rare one.
        //
        // The property is named for the EVENT rather than the setting — `EndedByDeadline`, not
        // `RoundDeadline` — because the first draft carried the limit on every summary and this
        // test could not say what it meant: a round that finished in time has no deadline to
        // report, whatever bound it was running under.
        Answered(3).Sentence.Should().Be("all 3 reviewers answered");
        Answered(3).EndedByDeadline.Should().BeNull("a round that finished in time was not ended by one");
    }
}
