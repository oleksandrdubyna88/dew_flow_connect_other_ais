using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A chosen data directory is partitioned per SIDE, so two of them can share one location.
/// </summary>
/// <remarks>
/// <para><b>What this is for (issue #115).</b> The data directory should be movable somewhere that
/// survives a Windows reinstall — a NAS. The operator's case is that Windows writes to its directory
/// and WSL to its own, and both are then pointed at the same shared place. The decision was
/// deliberately NOT to merge them: each side keeps its own database, side by side.</para>
///
/// <para><b>The default does not move.</b> With no <c>COAI_DATA_DIR</c> the path is what it has
/// always been. The per-side layout is a property of a directory somebody chose to share.</para>
///
/// <para><b>There is no flat-layout fallback, and that is the whole correctness of this.</b> The
/// plan's first draft said an override whose directory already holds <c>coai.db</c> keeps using that
/// directory, so an existing overrider would not find an empty one. Three reviewers independently
/// pointed out what that does in the operator's own scenario: Windows moves its directory to the NAS
/// root, so the database sits there; WSL is pointed at the same root, sees it, adopts the flat
/// layout — and both sides write one SQLite file, which is exactly what this partition exists to
/// prevent. An override always resolves to <c>&lt;dir&gt;/&lt;side&gt;</c>; a database in the root is
/// reported, never adopted.</para>
/// </remarks>
public sealed class EachSideKeepsItsOwnDataDirectoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("coai-side-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives one run is litter, not a failed test.
        }
    }

    /// <summary>Settings built from an environment of exactly these variables.</summary>
    private static PanelSettings From(params (string Name, string Value)[] vars) =>
        PanelSettings.FromEnvironment(name =>
            vars.FirstOrDefault(v => v.Name == name) is { Name: not null } hit ? hit.Value : null);

    [Fact]
    public void WithNoOverride_TheDirectoryIsExactlyWhatItHasAlwaysBeen()
    {
        // The promise that nobody has to do anything, as a test rather than a sentence.
        PanelSettings.FromEnvironment(_ => null).DataDir.Should().Be(PanelSettings.DefaultDataDir);
    }

    [Fact]
    public void AChosenDirectoryWithNoSideAsked_IsUsedExactlyAsChosen()
    {
        // The partition is OPT-IN, and this is the test that says so. The first build applied it to
        // every override and turned six scenario tests red — they set COAI_DATA_DIR and read files
        // from that exact path, as a script or the bench or a year-old setting also would.
        From(("COAI_DATA_DIR", _root)).DataDir.Should().Be(Path.GetFullPath(_root));
    }

    [Fact]
    public void AskingForTheDerivedSide_PartitionsWithoutNamingOne()
    {
        var dir = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "auto")).DataDir;

        dir.Should().StartWith(Path.GetFullPath(_root));
        dir.Should().NotBe(Path.GetFullPath(_root));
        Path.GetFileName(dir).Should().Be(PanelSettings.DataSide(_ => null));
    }

    [Fact]
    public void TwoSidesGivenOneLocation_ResolveToDifferentDirectories()
    {
        var windows = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "windows-desktop01"));
        var wsl = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "linux-desktop01"));

        windows.DataDir.Should().NotBe(wsl.DataDir, "two sides on one NAS must not share a database");
        windows.DataDir.Should().Be(Path.Combine(Path.GetFullPath(_root), "windows-desktop01"));
        wsl.DataDir.Should().Be(Path.Combine(Path.GetFullPath(_root), "linux-desktop01"));
    }

    /// <summary>
    /// The scenario the plan round found: a database already in the shared root is NOT adopted.
    /// </summary>
    [Fact]
    public void ADatabaseInTheSharedRoot_IsNeverAdoptedByEitherSide()
    {
        File.WriteAllText(Path.Combine(_root, "coai.db"), "not really a database, but it is there");

        var windows = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "windows-desktop01"));
        var wsl = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "linux-desktop01"));

        windows.DataDir.Should().NotBe(Path.GetFullPath(_root),
            "adopting the flat layout is how both sides end up writing one file");
        windows.DataDir.Should().NotBe(wsl.DataDir);
    }

    [Fact]
    public void ADatabaseInTheSharedRoot_IsReportedWithWhatToDoAboutIt()
    {
        File.WriteAllText(Path.Combine(_root, "coai.db"), "x");

        var settings = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "windows-desktop01"));

        settings.Unrecognised.Should().ContainSingle(n => n.Contains("coai.db") && n.Contains(_root),
            "history left in the old flat layout must not go invisible");
    }

    [Fact]
    public void ASideDirectoryCreatedForTheFirstTime_IsReported()
    {
        // A mistyped NAS path is a creatable directory. Somebody who typo'd it would otherwise
        // accumulate a second history quietly while believing they were writing to the first.
        var settings = From(("COAI_DATA_DIR", Path.Combine(_root, "probably-a-typo")), ("COAI_DATA_SIDE", "s"));

        settings.Unrecognised.Should().Contain(n => n.Contains(settings.DataDir));
    }

    [Theory]
    [InlineData("../shared")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("   ")]
    public void ASideNameThatCouldEscapeTheRoot_IsRefused(string side)
    {
        var settings = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", side));

        settings.DataDir.Should().StartWith(Path.GetFullPath(_root),
            $"a side called '{side}' must not escape the root it is meant to partition");
        Path.GetFullPath(settings.DataDir).Should().NotBe(Path.GetFullPath(Path.Combine(_root, "..")));
    }

    [Fact]
    public void TwoWslDistributionsOnOneHost_AreTwoSides()
    {
        // Both report linux-<hostname>, because WSL takes its hostname from the Windows host. The
        // distribution is what separates them. Raised on the plan round.
        var ubuntu = PanelSettings.DataSide(n => n == "WSL_DISTRO_NAME" ? "Ubuntu" : null);
        var debian = PanelSettings.DataSide(n => n == "WSL_DISTRO_NAME" ? "Debian" : null);

        ubuntu.Should().NotBe(debian);
        ubuntu.Should().Contain("ubuntu");
    }

    [Fact]
    public void ASideNameIsPathSafeAndNeverEndsInASeparator()
    {
        var side = PanelSettings.DataSide(_ => null);

        side.Should().NotBeNullOrWhiteSpace();
        side.Should().NotContain("/").And.NotContain("\\");
        side.Should().NotEndWith("-", "an empty machine name must not leave a dangling dash");
    }

    [Fact]
    public void ATeamServerToken_LandsInsideTheSidesOwnDirectory()
    {
        var settings = From(("COAI_DATA_DIR", _root), ("COAI_DATA_SIDE", "windows-desktop01"));

        var token = Runners.Reviewers.TeamServerAuth.TokenPath(settings.DataDir, "https://coai.example.dev");

        token.Should().StartWith(settings.DataDir,
            "a token belongs to the side that signed in, and must not be visible to the other");
    }
}
