using Xunit;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The shipped defaults must live INSIDE the binary.
/// </summary>
/// <remarks>
/// Found by the first real run (2026-08-31): the release asset carries one file — the executable —
/// while the prompts shipped as content copied beside it. In the test output they were present
/// (the project reference copies them), so 125 green tests said nothing about it; installed from
/// the release, every `review_plan` died with "An error occurred invoking 'review_plan'".
/// </remarks>
public sealed class RolePromptsTests
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-prompts-").FullName;

    [Theory]
    [InlineData(ReviewRole.PlanCritique, "PLAN")]
    [InlineData(ReviewRole.Architecture, "ARCHITECTURE reviewer")]
    [InlineData(ReviewRole.SecurityReliability, "SECURITY AND RELIABILITY")]
    [InlineData(ReviewRole.UxDxPerformance, "CODE ONLY: no browser")]
    public void ShippedDefault_ComesFromTheAssembly_NotTheFilesystem(ReviewRole role, string marker)
    {
        // Embedded, so this holds for a binary installed ALONE from a release asset.
        RolePrompts.ShippedDefaultFor(role).Should().Contain(marker);
    }

    [Fact]
    public void EveryDefault_AsksForTheHonestEmptyAnswer()
    {
        foreach (var role in Enum.GetValues<ReviewRole>())
        {
            RolePrompts.ShippedDefaultFor(role).Should().Contain("empty findings list",
                "a reviewer told to always find something will always find something");
        }
    }

    [Fact]
    public void OverrideWins_AndRestoreBringsTheShippedTextBack()
    {
        var prompts = new RolePrompts(_data);
        var shipped = prompts.For(ReviewRole.Architecture);

        prompts.Override(ReviewRole.Architecture, "review it my way");
        prompts.For(ReviewRole.Architecture).Should().Be("review it my way");

        prompts.RestoreDefault(ReviewRole.Architecture);
        prompts.For(ReviewRole.Architecture).Should().Be(shipped, "restore is byte-exact");
    }

    [Fact]
    public void RestoringAnUnoverriddenRole_IsNotAnError()
    {
        var prompts = new RolePrompts(_data);
        var act = () => prompts.RestoreDefault(ReviewRole.PlanCritique);

        act.Should().NotThrow();
    }
}
