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
    public void EveryRole_HasExactlyOneUniversalPrompt_AndTwoNarrowOnes()
    {
        foreach (var role in Roles)
        {
            var choices = PromptCatalog.For(role).ToList();
            // A universal prompt and two lenses, plus — for the three CODE roles — the conventions
            // pass, which is not a lens: it asks a different question (does this obey the written
            // rules) and it owns round 1 rather than taking a turn in the rotation.
            var expected = role == PromptCatalog.PlanRole ? 3 : 4;
            choices.Should().HaveCount(expected, $"{role} should offer a universal prompt and two lenses");
            choices.Count(c => c.Universal).Should().Be(1, $"{role} needs exactly one default");
            choices.Count(c => c.Id == PromptCatalog.ConventionsId)
                .Should().Be(role == PromptCatalog.PlanRole ? 0 : 1, "a plan is not judged against code conventions");
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
