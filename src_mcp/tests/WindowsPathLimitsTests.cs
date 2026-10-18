using System.Text.RegularExpressions;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Windows' path limits are ONE named pair of constants, and every C# caller reads them
/// (<c>.agents/conventions/common/platform-limits.md</c> §1 item 4; <c>research/PLAN_one_windows_path_limit.md</c>).
/// </summary>
/// <remarks>
/// The pins hold the probed numbers as literals on purpose: a pin that read the constant could not go red when the
/// constant is wrong. The censuses use <see cref="ProductionSources"/>, which removes comments and string contents
/// first, so prose that quotes 259 is not a second copy of it.
/// </remarks>
public sealed class WindowsPathLimitsTests
{
    private const string TheOneType = "src_mcp/runners/Platform/WindowsPathLimits.cs";

    /// <summary>A whole-number 259 or 247 — not part of a name, not part of a decimal.</summary>
    private static readonly Regex TheLimitAsALiteral =
        new(@"(?<![\w.])(?:259|247)(?![\w.])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheFileLimitIsTheProbed259_AndTheFolderLimitTheProbed247()
    {
        WindowsPathLimits.FileUnits.Should().Be(259, "P1 created a 259-unit file path and P2 refused 260");
        WindowsPathLimits.FolderUnits.Should().Be(247, "git for Windows made a 247-unit folder and refused 248");
    }

    [Fact]
    public void TheVibeBudget_TakesItsFileLimitFromTheOneType()
    {
        VibePathBudget.Windows.FileLimit.Should().Be(WindowsPathLimits.FileUnits);
    }

    [Fact]
    public void BothCallers_ReadTheOneType()
    {
        var readers = ProductionSources.FilesMentioning("WindowsPathLimits.");

        readers.Should().ContainKey("src_mcp/runners/Vibe/VibePathBudget.cs")
            .And.ContainKey("src_mcp/runners/Worktrees/WorktreeFailure.cs");
    }

    [Fact]
    public void NoOtherProductionCode_SpellsTheLimit()
    {
        var spelled = ProductionSources.FilesMatching(TheLimitAsALiteral);

        spelled.Should().ContainKey(TheOneType, "the scanner must see the one place, or an empty result proves nothing");
        spelled.Keys.Should().BeEquivalentTo(
            [TheOneType],
            "a call site that keeps its own 259 or 247 is the copy that stays wrong when the constant is corrected");
        spelled[TheOneType].Should().Be(2, "the file limit and the folder limit, once each");
    }
}
