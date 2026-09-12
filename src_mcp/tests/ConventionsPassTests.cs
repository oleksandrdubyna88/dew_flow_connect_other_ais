using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The conventions pass: what it judges, and where it lives.
/// </summary>
/// <remarks>
/// <para>Three reviewers already cover architecture, security and performance, each with its own
/// taste. The one thing none of them was doing is holding the change to the standard the project
/// WROTE DOWN — and that is the standard its human authors are held to, so the two halves were
/// being judged differently by construction.</para>
/// <para><b>It is a ROLE since 2026-09-08, not a prompt the other code roles can be given.</b> As a
/// prompt it had no budget: it borrowed somebody's round — first every code role's round 1, then
/// Architecture's alone — so giving it a round took one away from architecture or security, and its
/// findings counted against that role's threshold. It behaved like a role, and now it is one.</para>
/// <para>Its twin on the panel side is <c>panelServerPromptAgreement.test.ts</c>. Two suites for one
/// rule, because the rule is that two programs agree and neither can check that alone.</para>
/// </remarks>
public sealed class ConventionsPassTests
{
    private static readonly IReadOnlyList<string> NoChoice = [];

    [Fact]
    public void TheConventionsPrompt_BelongsToItsOwnRole_AndToNoOther()
    {
        RoleCatalog.Builtin.For(RoleCatalog.ConventionsRole)
            .Should().ContainSingle(p => p.Id == RoleCatalog.ConventionsId,
                "the Conventions role has exactly one prompt, which is therefore its universal one");

        foreach (var role in (string[])[RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole, RoleCatalog.PlanRole])
        {
            RoleCatalog.Builtin.For(role).Should().NotContain(p => p.Id == RoleCatalog.ConventionsId,
                $"{role} no longer offers the conventions prompt — that was the point of the role");
        }
    }

    [Fact]
    public void TheConventionsRole_RunsItsOwnPrompt_OnEveryRound()
    {
        // No round is special any more. The role IS the pass, so round 1 and round 3 are the same
        // question asked twice rather than a substitution that only happens once.
        foreach (var round in (int[])[1, 2, 3])
        {
            RoleCatalog.Builtin.ForRound(RoleCatalog.ConventionsRole, round, NoChoice)
                .Id.Should().Be(RoleCatalog.ConventionsId);
        }
    }

    /// <summary>
    /// The branch that is GONE, asserted so it cannot come back by accident.
    /// </summary>
    /// <remarks>
    /// `ForRound` used to substitute the conventions pass into round 1 of a code role when the
    /// repository had written rules. Every code role now opens on its own subject, and whether the
    /// repository wrote anything down decides whether the Conventions ROLE runs at all — a question
    /// for the caller assembling the round, not for a prompt lookup.
    /// </remarks>
    [Fact]
    public void RoundOne_IsNoLongerSpecial_ForAnyRole()
    {
        foreach (var role in (string[])[RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole, RoleCatalog.PlanRole])
        {
            var universal = RoleCatalog.Builtin.For(role).First(p => p.Universal).Id;

            RoleCatalog.Builtin.ForRound(role, 1, NoChoice).Id.Should().Be(universal,
                $"{role} round 1 is {role}'s own question now");
            RoleCatalog.Builtin.ForRound(role, 2, NoChoice).Id.Should().Be(universal);
        }
    }

    /// <summary>
    /// A repository that wrote nothing down does not get a conventions reviewer — and the round is
    /// otherwise untouched.
    /// </summary>
    /// <remarks>
    /// <para>The behaviour existed for an hour with NO test, which a review of the round caught. It
    /// was unreachable except through <c>ReviewCodeAsync</c>, which needs a session, a checkout and
    /// a git repository — so it stayed unasserted rather than being asserted badly. Extracting the
    /// decision into a pure function is what made it a test instead of an integration fixture.</para>
    /// <para>A reviewer that invents a standard is worse than no reviewer: the finding it raises
    /// cannot be argued with, because there is no sentence to point at.</para>
    /// </remarks>
    [Fact]
    public void WithNoWrittenRules_TheConventionsReviewersAreDropped_AndNobodyElseIs()
    {
        string[] scheduled =
            [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole];

        PanelService.RolesWithRulesInMind(scheduled, hasRules: false)
            .Should().Equal([RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole]);
    }

    [Fact]
    public void WithWrittenRules_TheRoundIsExactlyWhatWasScheduled()
    {
        string[] scheduled = [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole];

        PanelService.RolesWithRulesInMind(scheduled, hasRules: true).Should().Equal(scheduled);
    }

    [Fact]
    public void ARoundThatNeverScheduledConventions_IsUntouchedEitherWay()
    {
        // The plan stage, and any code round whose Conventions budget is spent. Dropping nothing
        // must not be spelled as a special case anywhere.
        string[] scheduled = [RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole];

        PanelService.RolesWithRulesInMind(scheduled, hasRules: false).Should().Equal(scheduled);
        PanelService.RolesWithRulesInMind(scheduled, hasRules: true).Should().Equal(scheduled);
    }

    /// <summary>
    /// The sentence that names where to put rules names every place that is actually read.
    /// </summary>
    /// <remarks>
    /// It was hand-written and listed four of the six, omitting `.github/copilot-instructions.md`
    /// and `.cursor/rules` — so it told somebody with no rules to write a file the reader never
    /// opens. The list is derived from `RuleFiles`' own two now.
    /// </remarks>
    [Fact]
    public void TheSourcesNamedToAPersonAreTheSourcesActuallyRead()
    {
        RuleFiles.SourceNames.Should().Contain(
            ["CLAUDE.md", "AGENTS.md", "GEMINI.md", ".github/copilot-instructions.md", ".claude/rules", ".cursor/rules"]);
    }

    [Fact]
    public void AnExplicitChoice_StillWins()
    {
        // A default, not a lock — the rule that survived every reshaping of this pass.
        RoleCatalog.Builtin.ForRound(RoleCatalog.ArchitectureRole, 1, ["arch-boundaries"])
            .Id.Should().Be("arch-boundaries");
    }

    /// <summary>
    /// The saved configuration of somebody who chose `Conventions` under Architecture yesterday.
    /// </summary>
    /// <remarks>
    /// Two reviewers on the plan round asked what happens to that stored selection now that the
    /// prompt has left the three code roles. Nothing needs to happen: an id that is not in the
    /// role's list falls back to the role's own universal question, which is the same rule that
    /// covers a renamed prompt. It is asserted by NAME here because "a stale value falls back" is
    /// the generic sentence, and this is the specific selection this change orphaned.
    /// </remarks>
    [Fact]
    public void AStoredConventionsChoice_UnderARoleThatNoLongerOffersIt_FallsBackToThatRolesOwnQuestion()
    {
        foreach (var role in (string[])[RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole])
        {
            RoleCatalog.Builtin.ForRound(role, 1, [RoleCatalog.ConventionsId])
                .Id.Should().Be(RoleCatalog.Builtin.For(role).First(p => p.Universal).Id,
                    $"{role} no longer offers the conventions prompt, so a saved choice of it is stale");
        }
    }

    [Fact]
    public void AnUnknownChoice_FallsBackRatherThanLeavingTheRoundWithNothing()
    {
        RoleCatalog.Builtin.ForRound(RoleCatalog.SecurityRole, 1, ["a-prompt-that-was-renamed"])
            .Id.Should().Be(RoleCatalog.Builtin.For(RoleCatalog.SecurityRole).First(p => p.Universal).Id);
    }

    [Fact]
    public void ThePlanStage_IsUntouched_BecauseAPlanIsNotADiff()
    {
        RoleCatalog.Builtin.ForRound(RoleCatalog.PlanRole, 1, NoChoice)
            .Id.Should().Be("plan-critique");
    }

    /// <remarks>
    /// The open tail of the conventions-is-its-own-role plan. A round in a repository with no written
    /// rules drops its Conventions reviewers — correctly, because a conventions pass with nothing to
    /// judge against would invent a standard — and the ONLY place that was said was the server's own
    /// log. The AI that called the gate was handed a thinner round and no sentence saying why.
    /// </remarks>
    [Fact]
    public void ARoundThatSkippedARole_SaysSoToTheCaller()
    {
        var summary = ReviewerSummary.AllAnswered(3) with
        {
            NotAsked = [new SkippedRole("Conventions", "this repository has no written rules to judge against")],
        };

        summary.Sentence.Should().Contain("Conventions")
            .And.Contain("not asked")
            .And.Contain("no written rules");
    }

    [Fact]
    public void ASkippedRole_DoesNotReadAsAFailure()
    {
        // A reviewer that could not run is a problem; a role nobody asked for is a decision. Reported
        // in the same words, a correct round reads as a degraded one.
        var summary = ReviewerSummary.AllAnswered(3) with
        {
            NotAsked = [new SkippedRole("Conventions", "this repository has no written rules to judge against")],
        };

        summary.Sentence.Should().StartWith("all 3 reviewers answered");
        summary.Sentence.Should().NotContain("could not run");
        summary.Sentence.Should().NotContain("failed");
    }

    [Fact]
    public void ARoundThatSkippedNothing_ReadsExactlyAsItAlwaysDid()
    {
        // This file's own rule, as an assertion: an addition for the rare round must not change the
        // sentence of every ordinary one.
        ReviewerSummary.AllAnswered(4).Sentence.Should().Be("all 4 reviewers answered");
        new ReviewerSummary(4, 3, ["codex/Architecture: timeout"]).Sentence
            .Should().Be("3 of 4 reviewers answered; failed: codex/Architecture: timeout");
    }

    [Fact]
    public void EveryClauseAtOnce_KeepsItsOrder_WithTheSkipLast()
    {
        // A deadline explains the failures, the failures explain the count, and what was never asked
        // for is last because it is the only one of the three that is not a problem.
        var summary = new ReviewerSummary(4, 2, ["codex/Architecture: timeout"], ["gemini/Conventions: no key"])
        {
            EndedByDeadline = TimeSpan.FromMinutes(10),
            NotAsked = [new SkippedRole("Conventions", "this repository has no written rules to judge against")],
        };

        summary.Sentence.Should().Be(
            "2 of 4 reviewers answered; failed: codex/Architecture: timeout; "
            + "the round reached its 10 minute limit and the reviewers still running were cancelled; "
            + "1 enabled reviewer could not run: gemini/Conventions: no key; "
            + "Conventions was not asked: this repository has no written rules to judge against");
    }

    [Fact]
    public void EveryOmittedRole_CarriesTheReasonItsOwnRuleGaveIt()
    {
        // The code round's finding, and the trap it names: mapping the DIFFERENCE onto one reason
        // works while there is one rule, and tells the caller the wrong thing with complete
        // confidence the day there are two. The pairing lives beside the rule that produces it.
        IReadOnlyList<string> scheduled =
            [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole];

        PanelService.RolesNotAsked(scheduled, hasRules: true).Should().BeEmpty();

        var skipped = PanelService.RolesNotAsked(scheduled, hasRules: false);
        skipped.Should().ContainSingle();
        skipped[0].Role.Should().Be(RoleCatalog.ConventionsRole);
        skipped[0].Reason.Should().Be(PanelService.NoWrittenRules);
        // Derived from the filter, never written out beside it: what is not asked and what ran are
        // the same decision read twice.
        skipped.Select(s => s.Role).Should()
            .NotIntersectWith(PanelService.RolesWithRulesInMind(scheduled, hasRules: false).Select(r => r.ToString()));
    }
}
