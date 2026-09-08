using CoaiMcp.Core.Rounds;
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
        PromptCatalog.For(PromptCatalog.ConventionsRole)
            .Should().ContainSingle(p => p.Id == PromptCatalog.ConventionsId,
                "the Conventions role has exactly one prompt, which is therefore its universal one");

        foreach (var role in (string[])[PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole, PromptCatalog.PlanRole])
        {
            PromptCatalog.For(role).Should().NotContain(p => p.Id == PromptCatalog.ConventionsId,
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
            PromptCatalog.ForRound(PromptCatalog.ConventionsRole, round, NoChoice)
                .Id.Should().Be(PromptCatalog.ConventionsId);
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
        foreach (var role in (string[])[PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole, PromptCatalog.PlanRole])
        {
            var universal = PromptCatalog.For(role).First(p => p.Universal).Id;

            PromptCatalog.ForRound(role, 1, NoChoice).Id.Should().Be(universal,
                $"{role} round 1 is {role}'s own question now");
            PromptCatalog.ForRound(role, 2, NoChoice).Id.Should().Be(universal);
        }
    }

    [Fact]
    public void AnExplicitChoice_StillWins()
    {
        // A default, not a lock — the rule that survived every reshaping of this pass.
        PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, 1, ["arch-boundaries"])
            .Id.Should().Be("arch-boundaries");
    }

    [Fact]
    public void AnUnknownChoice_FallsBackRatherThanLeavingTheRoundWithNothing()
    {
        PromptCatalog.ForRound(PromptCatalog.SecurityRole, 1, ["a-prompt-that-was-renamed"])
            .Id.Should().Be(PromptCatalog.For(PromptCatalog.SecurityRole).First(p => p.Universal).Id);
    }

    [Fact]
    public void ThePlanStage_IsUntouched_BecauseAPlanIsNotADiff()
    {
        PromptCatalog.ForRound(PromptCatalog.PlanRole, 1, NoChoice)
            .Id.Should().Be("plan-critique");
    }
}
