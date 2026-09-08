using System.Text.RegularExpressions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The catalog: which prompts exist, which one a round gets, and the two copies staying honest.
/// </summary>
public sealed class PromptCatalogTests
{
    private static readonly string[] Roles =
        [PromptCatalog.PlanRole, PromptCatalog.ArchitectureRole, PromptCatalog.SecurityRole, PromptCatalog.UxDxRole];

    [Fact]
    public void EveryRoleWithLenses_HasOneUniversalPromptAndFiveNarrowOnes()
    {
        // Six choices per section is the number a person was asked to be given, and the count is
        // pinned here rather than left to grow: a section with eleven options is a section nobody
        // reads, and every lens past the first three had to earn its place in a measurement
        // (research/RESULTS_focused_prompts.md).
        foreach (var role in Roles)
        {
            var choices = PromptCatalog.For(role).ToList();
            choices.Should().HaveCount(6, $"{role} should offer a universal prompt and five lenses");
            choices.Count(c => c.Universal).Should().Be(1, $"{role} needs exactly one default");
            choices.Should().NotContain(c => c.Id == PromptCatalog.ConventionsId,
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
        var choices = PromptCatalog.For(PromptCatalog.ConventionsRole).ToList();

        choices.Should().ContainSingle();
        choices[0].Id.Should().Be(PromptCatalog.ConventionsId);
        choices[0].Universal.Should().BeTrue();
    }

    [Fact]
    public void EveryPromptIdIsUniqueWithinItsRole_AndEveryLabelIsDistinct()
    {
        // Two entries sharing an id would make the panel's picker ambiguous and `ById` arbitrary;
        // two sharing a label would make it ambiguous to the person, which is the same defect one
        // layer up. Twelve entries were added at once, which is exactly when this stops being
        // obvious by inspection.
        foreach (var role in Roles)
        {
            var choices = PromptCatalog.For(role).ToList();
            choices.Select(c => c.Id).Should().OnlyHaveUniqueItems($"{role} has a duplicate prompt id");
            choices.Select(c => c.Label).Should().OnlyHaveUniqueItems($"{role} has two lenses with one name");
        }
    }

    [Fact]
    public void EveryPromptInTheCatalog_IsActuallyEmbeddedInTheBinary()
    {
        // A catalog entry with no file behind it is a picker option that fails at review time,
        // which is the worst possible moment to discover it.
        foreach (var choice in PromptCatalog.All)
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
                PromptCatalog.ForRound(role, round, []).Universal.Should().BeTrue();
            }
        }
    }

    [Fact]
    public void AnExplicitChoice_WinsOverTheDefault()
    {
        PromptCatalog.ForRound(PromptCatalog.SecurityRole, 2, ["", "sec-attack"])
            .Id.Should().Be("sec-attack");
    }

    [Fact]
    public void AnEmptyEntry_IsNotAChoice_SoTheDefaultStillApplies()
    {
        // The panel pads rounds nobody touched with "". If that counted as a choice, touching
        // round three would freeze rounds one and two — which is exactly what both reviewers of
        // the catalog commit caught. It has to fall THROUGH to the default, not resolve to the
        // empty id and leave the round with no prompt at all.
        PromptCatalog.ForRound(PromptCatalog.SecurityRole, 2, ["", ""])
            .Should().Be(PromptCatalog.UniversalFor(PromptCatalog.SecurityRole));
    }

    [Theory]
    [InlineData("no-such-prompt")]
    [InlineData("architecture")] // a real id, but belonging to another role
    public void AStaleOrForeignId_FallsThroughInsteadOfLeavingTheRoundWithNoPrompt(string id)
    {
        PromptCatalog.ForRound(PromptCatalog.SecurityRole, 1, [id])
            .Should().Be(PromptCatalog.UniversalFor(PromptCatalog.SecurityRole));
    }

    [Fact]
    public void ANullRoundList_IsNotAChoice_AndDoesNotCrashTheRound()
    {
        // `{"Architecture": null}` is valid JSON. Found by the gate reviewing the catalog commit:
        // it reached the round as a null list and was dereferenced there.
        var settings = PanelSettings.FromEnvironment(name =>
            name == "COAI_PROMPTS_PER_ROUND" ? """{"Architecture": null}""" : null);

        settings.PromptsPerRound.Should().NotContainKey("Architecture");
        var act = () => PromptCatalog.ForRound(
            PromptCatalog.ArchitectureRole, 1,
            settings.PromptsPerRound.GetValueOrDefault(PromptCatalog.ArchitectureRole, []));
        act.Should().NotThrow();
    }

    /// <summary>
    /// The extension mirrors this catalog, and a comment in it promises a test holds the two
    /// together. This is that test — the promise was written before it existed, which is exactly
    /// how the two halves of a mirrored list start to drift.
    /// </summary>
    [Fact]
    public void TheExtensionsCopyOfTheCatalog_ListsTheSamePrompts()
    {
        var mirror = Path.Combine(RepoRoot(), "src_vs_code", "src", "prompts.ts");
        File.Exists(mirror).Should().BeTrue($"the mirror should be at {mirror}");

        var ids = Regex.Matches(File.ReadAllText(mirror), @"id:\s*'([a-z0-9-]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        foreach (var choice in PromptCatalog.All)
        {
            ids.Should().Contain(choice.Id, "the panel cannot offer a prompt it does not know about");
        }
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
