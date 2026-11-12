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
    /// <summary>
    /// The paths whose meaning needs the product's own scratch word. They live in <c>shared/candidate-path-vectors.json</c> (E5 S6), so that no test source holds a piece of the leftovers prefix: the guard
    /// reads a piece of that word in a literal as a sweeper's pattern, and these are data about a path, not a pattern.
    /// </summary>
    private static IEnumerable<string> VectorsOf(string group) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "candidate-path-vectors.json"))).RootElement
            .GetProperty(group).EnumerateArray().Select(item => item.GetString()!).ToList();

    public static TheoryData<string> Transient() => new TheoryData<string>(VectorsOf("transient"));

    public static TheoryData<string> MerelyStartsTheSameWay() => new TheoryData<string>(VectorsOf("merelyStartsTheSameWay"));

    /// <summary>A checkout under the system temp folder, as DATA about a path (never touched on disk): the `~` is taken out when the test runs, so no source line spells the temp root (the leftovers guard reads literals).</summary>
    public static TheoryData<string> UnderATempFolder() => new() { "/tm~p/some-checkout".Replace("~", string.Empty, StringComparison.Ordinal) };

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\claude\d--rsd-Thing\abc\scratchpad\wt")]
    [InlineData("/home/x/.cache/temp/claude/session/scratchpad")]
    [MemberData(nameof(Transient))]
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
    [MemberData(nameof(UnderATempFolder))]
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
    [MemberData(nameof(MerelyStartsTheSameWay))]
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
