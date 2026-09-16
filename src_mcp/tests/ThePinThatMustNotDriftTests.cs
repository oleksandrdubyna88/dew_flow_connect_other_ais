using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// `TreeSitter.DotNet` stays where the operator pinned it, and a monthly bump has to argue with this.
/// </summary>
/// <remarks>
/// <para><b>The exemption used to be a sentence, and a sentence is not enforcement.</b> The package
/// was approved on 2026-09-15 on three conditions, the first of which was a hard version pin with no
/// floating updates. The pin was applied and the exemption from this family's monthly latest-stable
/// NuGet pass was written down nowhere that runs — so the next monthly sweep would have bumped it
/// with everything else and nothing would have objected. (Plan round, local reviewer: "the plan does
/// not specify the mechanism".)</para>
/// <para><b>Why this package and not the others.</b> The monthly pass exists because staying current
/// is cheaper than catching up, and it is right for a managed library. This one is different in two
/// ways that matter: it is published by an individual rather than an organisation, and what it
/// carries is not IL but <b>prebuilt native grammars for eight runtime identifiers</b>. A version
/// bump therefore changes the native binaries that `DropGrammarsNobodyParses` prunes by filename and
/// that the release workflow counts — neither of which a compiler can check. Upgrading is allowed and
/// expected; doing it <i>deliberately</i>, with those two checks re-run, is the whole point.</para>
/// <para>So: change the version here and in `Directory.Packages.props` together, in a commit that
/// says why, having watched the publish produce the right grammar count.</para>
/// </remarks>
public sealed class ThePinThatMustNotDriftTests
{
    /// <summary>The version the operator approved, and the one the pruning step is written against.</summary>
    private const string Approved = "1.3.0";

    [Fact]
    public void TreeSitterIsPinnedToTheApprovedVersion()
    {
        var props = Packages();

        props.Should().Contain(
            $"""<PackageVersion Include="TreeSitter.DotNet" Version="{Approved}" />""",
            "the package is exempt from the monthly NuGet bump — read this test's remarks before "
            + "changing the version, and re-run the publish's grammar count when you do");
    }

    /// <summary>The pin carries its reason where somebody bumping versions will read it.</summary>
    /// <remarks>
    /// A test asserting a version is a test somebody silences by editing the number. The comment
    /// beside the pin is what makes that person stop, so its presence is asserted too — the pairing
    /// is the mechanism, not either half of it.
    /// </remarks>
    [Fact]
    public void ThePinSaysWhyItIsPinned()
    {
        var props = Packages();

        props.Should().Contain(
            "published by one person", "the pin's reason must sit beside the pin, not only in a plan");
        props.Should().Contain(
            "prebuilt native", "and it must say what a bump would actually change");
    }

    private static string Packages()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "Directory.Packages.props");
            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            }
        }

        throw new FileNotFoundException("Directory.Packages.props was not found above the test binary");
    }
}
