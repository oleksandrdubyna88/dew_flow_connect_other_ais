using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The shipped defaults must live INSIDE the binary, and a prompt is found by its own id.
/// </summary>
/// <remarks>
/// <para>Found by the first real run (2026-08-31): the release asset carries one file — the
/// executable — while the prompts shipped as content copied beside it. In the test output they were
/// present (the project reference copies them), so 125 green tests said nothing about it; installed
/// from the release, every `review_plan` died with "An error occurred invoking 'review_plan'".</para>
/// <para>Keyed by PROMPT id since 2026-09-12. A role used to have exactly one prompt whose file name
/// this class knew by a switch statement; a role a person defines has prompts this build has never
/// heard of, and the file it wants is named by the prompt.</para>
/// </remarks>
public sealed class RolePromptsTests
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-prompts-").FullName;

    /// <summary>The general prompt of each shipped role, and a line only that prompt carries.</summary>
    [Theory]
    [InlineData("plan-critique", "PLAN")]
    [InlineData("architecture", "ARCHITECTURE reviewer")]
    [InlineData("security-reliability", "SECURITY AND RELIABILITY")]
    [InlineData("uxdx-performance", "CODE ONLY: no browser")]
    public void ShippedDefault_ComesFromTheAssembly_NotTheFilesystem(string promptId, string marker) =>
        // Embedded, so this holds for a binary installed ALONE from a release asset.
        RolePrompts.ShippedDefaultFor(promptId).Should().Contain(marker);

    /// <summary>
    /// Every prompt the catalog names, not every value of an enum.
    /// </summary>
    /// <remarks>
    /// It walked the enum, which had five values mapping onto four files — <c>Conventions</c> fell
    /// through a switch's default to the UX-DX prompt — so this checked four of the twenty-five
    /// texts and one of them twice. The seed knows what shipped, so the loop reads it, and the
    /// narrow lenses are held to the promise for the first time.
    ///
    /// Whitespace is collapsed before the search: the sentence is wrapped mid-phrase in twelve of
    /// the files, and a substring test that reads a line break as a missing instruction would have
    /// reported a defect in the prompts rather than in itself.
    /// </remarks>
    [Fact]
    public void EveryDefault_AsksForTheHonestEmptyAnswer()
    {
        foreach (var prompt in RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts))
        {
            OneLine(RolePrompts.ShippedDefaultFor(prompt.Id)).Should().Contain("empty findings list",
                $"{prompt.Id}: a reviewer told to always find something will always find something");
        }
    }

    private static string OneLine(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    [Fact]
    public void OverrideWins_AndRestoreBringsTheShippedTextBack()
    {
        var prompts = new RolePrompts(_data);
        var architecture = RoleCatalog.Builtin.UniversalFor(RoleCatalog.ArchitectureRole).Id;
        var shipped = prompts.For(architecture);

        prompts.Override(architecture, "review it my way");
        prompts.For(architecture).Should().Be("review it my way");

        prompts.RestoreDefault(architecture);
        prompts.For(architecture).Should().Be(shipped, "restore is byte-exact");
    }

    [Fact]
    public void ANarrowLensHasItsOwnText_AndItsOwnOverride()
    {
        // The override layer was always keyed by prompt id underneath; what changed is that the
        // public surface admits it, which is what a role with prompts this build never shipped needs.
        var prompts = new RolePrompts(_data);
        prompts.For("arch-naming").Should().NotBe(prompts.For("architecture"));

        prompts.Override("arch-naming", "look at the names");
        prompts.For("arch-naming").Should().Be("look at the names");
        prompts.For("architecture").Should().NotBe("look at the names", "one lens's override is not another's");
    }

    [Fact]
    public void RestoringAnUnoverriddenPrompt_IsNotAnError()
    {
        var prompts = new RolePrompts(_data);
        var act = () => prompts.RestoreDefault("plan-critique");

        act.Should().NotThrow();
    }
}
