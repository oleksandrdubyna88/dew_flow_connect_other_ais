using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The first code round checks the project's own rules, and nothing else.
/// </summary>
/// <remarks>
/// <para>Three reviewers already cover architecture, security and performance, each with its own
/// taste. The one thing none of them was doing is holding the change to the standard the project
/// WROTE DOWN — and that is the standard its human authors are held to, so the two halves were
/// being judged differently by construction.</para>
/// <para>It takes round 1 because a violation of a written rule is the cheapest finding to act on
/// and the least arguable: there is a sentence to point at.</para>
/// </remarks>
public sealed class ConventionsPassTests
{
    private static readonly IReadOnlyList<string> NoChoice = [];

    [Fact]
    public void CodeRoundOne_IsTheConventionsPass_ForEveryCodeRole()
    {
        foreach (var role in (string[])[PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole])
        {
            PromptCatalog.ForRound(role, 1, NoChoice, rotating: false, hasRules: true)
                .Id.Should().Be("conventions", $"{role} round 1 must judge the written rules");
        }
    }

    [Fact]
    public void WithNoRulesInTheRepo_RoundOneIsTheUsualUniversalPrompt()
    {
        // A pass with nothing to judge against would invent a standard, which is worse than the
        // review it displaced.
        PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, 1, NoChoice, rotating: false, hasRules: false)
            .Id.Should().Be("architecture");
    }

    [Fact]
    public void ThePlanStage_IsUntouched_BecauseAPlanIsNotADiff()
    {
        PromptCatalog.ForRound(PromptCatalog.PlanRole, 1, NoChoice, rotating: false, hasRules: true)
            .Id.Should().Be("plan-critique");
    }

    [Fact]
    public void AnExplicitChoiceForRoundOne_StillWins()
    {
        // A default, not a lock: somebody who picked a lens for round 1 asked for that lens.
        PromptCatalog.ForRound(PromptCatalog.SecurityRole, 1, ["sec-attack"], rotating: false, hasRules: true)
            .Id.Should().Be("sec-attack");
    }

    [Fact]
    public void LaterRounds_AreTheLensesTheyAlwaysWere()
    {
        PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, 2, NoChoice, rotating: false, hasRules: true)
            .Id.Should().Be("architecture", "only round 1 is claimed");
        PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, 2, NoChoice, rotating: true, hasRules: true)
            .Id.Should().Be("arch-boundaries", "and rotation still owns the rest");
    }

    [Fact]
    public void TheConventionsPrompt_IsInTheCatalog_ForEachCodeRole()
    {
        foreach (var role in (string[])[PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole])
        {
            PromptCatalog.For(role).Should().Contain(p => p.Id == "conventions",
                "a person must be able to choose it for any round, or unchoose it");
        }
    }
}
