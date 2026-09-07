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

    /// <summary>
    /// ARCHITECTURE round 1, and only Architecture — narrowed 2026-09-07 from all three code roles.
    /// </summary>
    /// <remarks>
    /// <para>Running it on all three showed the cost: the same written rules read by three reviewers
    /// in the same round produce the same findings three times, and a role whose budget is ONE round
    /// spent it on conventions and never asked its own question at all. Architecture keeps the pass
    /// because it has two rounds — the rules, then the broad question — so nothing it used to ask is
    /// lost.</para>
    /// <para>The other two reach it by choosing it: this is a default, not a lock, and the picker
    /// still offers `Conventions` for every code role.</para>
    /// </remarks>
    [Fact]
    public void CodeRoundOne_IsTheConventionsPass_ForArchitectureAlone()
    {
        PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, 1, NoChoice, hasRules: true)
            .Id.Should().Be("conventions", "Architecture round 1 judges the written rules");

        foreach (var role in (string[])[PromptCatalog.SecurityRole, PromptCatalog.UxDxRole])
        {
            PromptCatalog.ForRound(role, 1, NoChoice, hasRules: true)
                .Id.Should().Be(
                    PromptCatalog.For(role).First(p => p.Universal).Id,
                    $"{role} has one round by default and spends it on its own subject");
        }
    }

    [Fact]
    public void TheConventionsPass_IsStillOfferedToEveryCodeRole()
    {
        // Narrowing the DEFAULT must not narrow the choice: a person who wants the rules read three
        // times can still say so, and the panel's picker lists this prompt for all three.
        foreach (var role in (string[])[PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole])
        {
            PromptCatalog.ForRound(role, 1, [PromptCatalog.ConventionsId], hasRules: true)
                .Id.Should().Be("conventions", $"{role} can be asked for the conventions pass");
        }
    }

    [Fact]
    public void WithNoRulesInTheRepo_RoundOneIsTheUsualUniversalPrompt()
    {
        // A pass with nothing to judge against would invent a standard, which is worse than the
        // review it displaced.
        PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, 1, NoChoice, hasRules: false)
            .Id.Should().Be("architecture");
    }

    [Fact]
    public void ThePlanStage_IsUntouched_BecauseAPlanIsNotADiff()
    {
        PromptCatalog.ForRound(PromptCatalog.PlanRole, 1, NoChoice, hasRules: true)
            .Id.Should().Be("plan-critique");
    }

    [Fact]
    public void AnExplicitChoiceForRoundOne_StillWins()
    {
        // A default, not a lock: somebody who picked a lens for round 1 asked for that lens.
        PromptCatalog.ForRound(PromptCatalog.SecurityRole, 1, ["sec-attack"], hasRules: true)
            .Id.Should().Be("sec-attack");
    }

    [Fact]
    public void OnlyRoundOne_IsClaimed_AndOnlyForACodeRole()
    {
        // The conventions pass takes round 1 and nothing else. A later round with no explicit pick
        // is that role's universal prompt — the same answer the PANEL shows for it, which is the
        // agreement `panelServerPromptAgreement.test.ts` holds from the other side.
        foreach (var round in (int[])[2, 3, 4])
        {
            PromptCatalog.ForRound(PromptCatalog.ArchitectureRole, round, NoChoice, hasRules: true)
                .Id.Should().Be("architecture", $"round {round} is not claimed");
        }
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
