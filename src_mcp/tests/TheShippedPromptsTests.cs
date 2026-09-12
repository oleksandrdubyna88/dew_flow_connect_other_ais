using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shipped prompts: which exist, which one a round gets, and that each has text behind it.
/// </summary>
/// <remarks>
/// <para>Was <c>PromptCatalogTests</c>, over the C# array these rows used to live in. That array is
/// gone — the rows are <c>shared/builtin-roles.json</c> now, loaded as <see cref="RoleCatalog"/> —
/// and the behaviour it guarded moved with it rather than being deleted alongside.</para>
/// <para>One test did NOT move: <c>TheExtensionsCopyOfTheCatalog_ListsTheSamePrompts</c>, which read
/// <c>prompts.ts</c> with a regular expression to check the panel offered the same ids. There is no
/// second copy to check any more. Each half now asserts its own LOADER against the seed —
/// <c>BuiltinRoleCatalogTests</c> here, <c>builtinRoleCatalog.test.ts</c> there — so a seed edit
/// either side misses goes red on that side, for the reason it actually happened, instead of a
/// regular expression over somebody else's source going quiet after a reformat.</para>
/// </remarks>
public sealed class TheShippedPromptsTests
{
    /// <summary>The roles with a universal prompt and five lenses — every shipped role but Conventions.</summary>
    private static readonly string[] Roles =
        [RoleCatalog.PlanRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole];

    private static RoleCatalog Shipped => RoleCatalog.Builtin;

    [Fact]
    public void EveryRoleWithLenses_HasOneUniversalPromptAndFiveNarrowOnes()
    {
        // Six choices per section is the number a person was asked to be given, and the count is
        // pinned here rather than left to grow: a section with eleven options is a section nobody
        // reads, and every lens past the first three had to earn its place in a measurement
        // (research/RESULTS_focused_prompts.md).
        foreach (var role in Roles)
        {
            var choices = Shipped.For(role).ToList();
            choices.Should().HaveCount(6, $"{role} should offer a universal prompt and five lenses");
            choices.Count(c => c.Universal).Should().Be(1, $"{role} needs exactly one default");
            choices.Should().NotContain(c => c.Id == RoleCatalog.ConventionsId,
                $"{role} does not offer the conventions prompt — Conventions is a role of its own");
        }
    }

    /// <summary>
    /// The Conventions role has ONE prompt, and that is the shape rather than an omission.
    /// </summary>
    /// <remarks>
    /// It asks a different kind of question from the lenses: not "look at the change through this
    /// aperture" but "does this obey what the project wrote down". There is nothing to choose
    /// between, so the picker has one entry and it is the role's universal prompt.
    /// </remarks>
    [Fact]
    public void TheConventionsRole_HasExactlyOnePrompt_WhichIsItsUniversalOne()
    {
        var choices = Shipped.For(RoleCatalog.ConventionsRole).ToList();

        choices.Should().ContainSingle();
        choices[0].Id.Should().Be(RoleCatalog.ConventionsId);
        choices[0].Universal.Should().BeTrue();
    }

    [Fact]
    public void EveryPromptIdIsUniqueWithinItsRole_AndEveryLabelIsDistinct()
    {
        // Two entries sharing an id would make the panel's picker ambiguous and `PromptById`
        // arbitrary; two sharing a label would make it ambiguous to the person, which is the same
        // defect one layer up. Twelve entries were added at once, which is exactly when this stops
        // being obvious by inspection.
        foreach (var role in Roles)
        {
            var choices = Shipped.For(role).ToList();
            choices.Select(c => c.Id).Should().OnlyHaveUniqueItems($"{role} has a duplicate prompt id");
            choices.Select(c => c.Label).Should().OnlyHaveUniqueItems($"{role} has two lenses with one name");
        }
    }

    [Fact]
    public void EveryPromptInTheCatalog_IsActuallyEmbeddedInTheBinary()
    {
        // A catalog entry with no file behind it is a picker option that fails at review time,
        // which is the worst possible moment to discover it. The seed carries what a picker SHOWS;
        // the text a reviewer reads is still an embedded `<id>.md`, and nothing but this holds the
        // two lists of twenty-five together.
        foreach (var choice in Shipped.Roles.SelectMany(r => r.Prompts))
        {
            var act = () => new RolePrompts(Path.GetTempPath()).ForChoice(choice);
            act.Should().NotThrow($"{choice.Id}.md must be an EmbeddedResource");
        }
    }

    [Fact]
    public void WithoutAChoice_EveryRoundGetsTheUniversalPrompt()
    {
        foreach (var role in Roles)
        {
            foreach (var round in (int[])[1, 2, 3, 7])
            {
                Shipped.ForRound(role, round, []).Universal.Should().BeTrue();
            }
        }
    }

    [Fact]
    public void AnExplicitChoice_WinsOverTheDefault() =>
        Shipped.ForRound(RoleCatalog.SecurityRole, 2, ["", "sec-attack"]).Id.Should().Be("sec-attack");

    [Fact]
    public void AnEmptyEntry_IsNotAChoice_SoTheDefaultStillApplies()
    {
        // The panel pads rounds nobody touched with "". If that counted as a choice, touching
        // round three would freeze rounds one and two — which is exactly what both reviewers of
        // the catalog commit caught. It has to fall THROUGH to the default, not resolve to the
        // empty id and leave the round with no prompt at all.
        Shipped.ForRound(RoleCatalog.SecurityRole, 2, ["", ""])
            .Should().Be(Shipped.UniversalFor(RoleCatalog.SecurityRole));
    }

    [Theory]
    [InlineData("no-such-prompt")]
    [InlineData("architecture")] // a real id, but belonging to another role
    public void AStaleOrForeignId_FallsThroughInsteadOfLeavingTheRoundWithNoPrompt(string id) =>
        Shipped.ForRound(RoleCatalog.SecurityRole, 1, [id])
            .Should().Be(Shipped.UniversalFor(RoleCatalog.SecurityRole));

    [Fact]
    public void ANullRoundList_IsNotAChoice_AndDoesNotCrashTheRound()
    {
        // `{"Architecture": null}` is valid JSON. Found by the gate reviewing the catalog commit:
        // it reached the round as a null list and was dereferenced there.
        var settings = PanelSettings.FromEnvironment(name =>
            name == "COAI_PROMPTS_PER_ROUND" ? """{"Architecture": null}""" : null);

        settings.PromptsPerRound.Should().NotContainKey("Architecture");
        var act = () => Shipped.ForRound(
            RoleCatalog.ArchitectureRole, 1,
            settings.PromptsPerRound.GetValueOrDefault(RoleCatalog.ArchitectureRole, []));
        act.Should().NotThrow();
    }
}
