using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The resolved data directory is a TYPE, so a root path cannot satisfy the writer's seam.
/// </summary>
/// <remarks>
/// <para>Story 1.3's reviewer asked for it and story 1.4 answered yes, in the narrowest form that
/// closes the hole: a record class whose constructor refuses an empty or relative path, minted by
/// <c>PanelSettings.DataDirectoryFor</c> and by nothing else. The thing it prevents is
/// <c>Append(DataRootFor(env), notice)</c> — the directory BEFORE the side is applied, which looks
/// like a data directory and is not one — and a structural test could not see that call; a
/// parameter type refuses it at compile time.</para>
/// <para>What it does NOT promise: that the directory is usable. <c>C:\data*</c> is rooted and
/// non-empty and fails at <c>CreateDirectory</c>; the writer's exception list is that guarantee, and
/// this type is only the part of it a compiler can hold.</para>
/// </remarks>
public sealed class ResolvedDataDirTests
{
    private static Func<string, string?> Env(string? dir, string? side) => name => name switch
    {
        "COAI_DATA_DIR" => dir,
        "COAI_DATA_SIDE" => side,
        _ => null,
    };

    [Fact]
    public void TheResolverMintsIt_AndItCarriesTheDirectoryTheResolverDecided()
    {
        // Read back from the function that computes it, never guessed: the side is applied, the
        // path is made full, and the value is what every other caller of the resolver gets.
        var root = Path.Combine(Path.GetTempPath(), "coai-resolved-" + Guid.NewGuid().ToString("N"));

        var resolved = PanelSettings.DataDirectoryFor(Env(root, "alpha"));

        resolved.Path.Should().Be(Path.Combine(Path.GetFullPath(root), "alpha"));
        SettingsFile.DataDirFrom(Env(root, "alpha")).Should().Be(resolved,
            "DataDirFrom is a pass-through of the same resolver and mints the same value");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPath_IsRefusedAtConstruction(string empty)
    {
        // `Path.Combine("", name)` is the bare file name, which lands beside whatever launched the
        // process. The type refuses it before any writer can be handed it.
        var constructing = () => new ResolvedDataDir(empty);

        constructing.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Theory]
    [InlineData("relative-dir")]
    [InlineData("sub/dir")]
    public void ARelativePath_IsRefusedAtConstruction(string relative)
    {
        // The resolver always answers a FULL path (`Path.GetFullPath`, or the rooted default), so a
        // relative one can only have been composed by hand — and a relative data directory is one
        // that moves with the working directory of whichever process was launched.
        var constructing = () => new ResolvedDataDir(relative);

        constructing.Should().Throw<ArgumentException>().WithMessage("*rooted*");
    }

    [Fact]
    public void ItValidatesTheShape_NotThatTheDirectoryIsUsable()
    {
        // Rooted and non-empty, and the OS will still refuse it. That is the writer's problem and
        // its exception list is what answers it; a type that tried to be the whole guarantee would
        // have to be perfect.
        var unusable = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "data*");

        var constructed = new ResolvedDataDir(unusable);

        constructed.Path.Should().Be(unusable);
    }

    [Fact]
    public void TwoResolutionsOfOneConfiguration_AreEqual()
    {
        var root = Path.Combine(Path.GetTempPath(), "coai-resolved-" + Guid.NewGuid().ToString("N"));

        PanelSettings.DataDirectoryFor(Env(root, "alpha"))
            .Should().Be(PanelSettings.DataDirectoryFor(Env(root, "alpha")),
                "a record compares by value, so two reads of one configuration are one directory");
        PanelSettings.DataDirectoryFor(Env(root, "alpha"))
            .Should().NotBe(PanelSettings.DataDirectoryFor(Env(root, "beta")));
    }

    [Fact]
    public void TheRootBeforeTheSide_IsDeliberatelyNotOneOfThese()
    {
        // `DataRootFor` keeps answering a string, and this pins the decision rather than leaving it
        // to be tidied away: it is the one thing in the codebase that looks like a data directory
        // and is not one, and giving it the type would make the type describe the confusion instead
        // of preventing it.
        typeof(PanelSettings).GetMethod(nameof(PanelSettings.DataRootFor))!.ReturnType
            .Should().Be(typeof(string));
        typeof(PanelSettings).GetMethod(nameof(PanelSettings.DataDirectoryFor))!.ReturnType
            .Should().Be(typeof(ResolvedDataDir));
        typeof(SettingsFile).GetMethod(nameof(SettingsFile.DataDirFrom))!.ReturnType
            .Should().Be(typeof(ResolvedDataDir));
    }
}
