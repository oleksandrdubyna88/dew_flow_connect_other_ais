using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which repository paths can have a history, decided before git is asked anything.
/// </summary>
/// <remarks>
/// 30 % of raw candidates live in scratch directories — 231 of 771 measured — and none of them has a
/// fix commit to find. Skipping them by name is cheaper than discovering it per finding, and it gives
/// the funnel a reason code instead of a shrug.
/// </remarks>
public sealed class CandidatePathTests
{
    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\claude\d--rsd-Thing\abc\scratchpad\wt")]
    [InlineData("/home/x/.cache/temp/claude/session/scratchpad")]
    [InlineData(@"D:\toDelete\Payment_endpoint")]
    [InlineData("D:/toDelete/Payment_endpoint")]
    public void AScratchDirectoryIsTransient(string path) =>
        CandidatePath.IsTransient(path).Should().BeTrue();

    /// <summary>
    /// A repository that merely SITS under a temp folder is not scratch.
    /// </summary>
    /// <remarks>
    /// The rule matched `/appdata/local/temp/` once, which refused this suite's own fixtures — every
    /// one of them is a temp subdirectory — before git was asked anything, and would refuse anybody
    /// who cloned somewhere temporary to try something out. A guard that cannot be tested is a guard
    /// nobody can trust.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\coai-collect-abc123")]
    [InlineData("/tmp/some-checkout")]
    [InlineData(@"D:\rsd\dew_flow_connect_other_ais")]
    [InlineData(@"D:\rsd\_wt\coai-consult")]
    public void AnOrdinaryRepositoryIsNot(string path) =>
        CandidatePath.IsTransient(path).Should().BeFalse();

    /// <summary>
    /// A directory whose name merely BEGINS with a scratch name is not a scratch directory.
    /// </summary>
    /// <remarks>
    /// The fragments were matched as substrings, so `/todelete` claimed `todelete_benchmarks` and
    /// `todelete-fixtures` — ordinary repositories, refused before git was asked anything and
    /// counted in the funnel as scratch. A whole path COMPONENT is what was meant, and what is now
    /// matched. Two reviewers found this; the second time after it had been accepted and not done.
    /// </remarks>
    [Theory]
    [InlineData(@"D:\rsd\todelete_benchmarks\repo")]
    [InlineData("/home/x/todelete-fixtures")]
    [InlineData(@"D:\work\temp\claudent\repo")]
    public void ANameThatMerelyStartsTheSameWayIsNot(string path) =>
        CandidatePath.IsTransient(path).Should().BeFalse();

    /// <summary>
    /// One repository spelled three ways is one repository.
    /// </summary>
    /// <remarks>
    /// Measured: 52 distinct `repo_path` strings collapse to 42, and `dew_flow_connect_other_ais` is
    /// stored as `D:\rsd\…`, `d:\rsd\…` and `D:/rsd/…`. Anything comparing paths has to canonicalise
    /// or it counts one checkout as three.
    /// </remarks>
    [Fact]
    public void ThreeSpellingsOfOnePathAgree()
    {
        var canonical = new[] { @"D:\rsd\thing", @"d:\rsd\thing", "D:/rsd/thing", "D:/rsd/thing/" }
            .Select(CandidatePath.Canonical)
            .Distinct(StringComparer.Ordinal);

        canonical.Should().ContainSingle().Which.Should().Be("d:/rsd/thing");
    }
}
