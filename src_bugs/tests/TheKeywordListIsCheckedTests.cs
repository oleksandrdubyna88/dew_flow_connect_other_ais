using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The check that decides whether this server starts at all.
/// </summary>
/// <remarks>
/// <para><b>It was covered, and then it was not.</b> The first version of these assertions called a
/// method named `Checked` that threw; the code round replaced it with `WhyUnusable`, which answers a
/// VALUE because the C# doctrine says expected failures are values — and the tests went with the old
/// name rather than following it. SonarCloud noticed as a coverage gap; what it actually is, is the
/// startup guard having no test.</para>
/// <para>What it guards: `/health` answers from a route that touches neither the keyword list nor
/// the database, so a binary that embedded an empty file would start, satisfy the release smoke,
/// publish, and then refuse every submission with "this server has no keyword list for CSharp" —
/// which reads like the contributor's fault and is not. The server exits <b>78</b> instead, and the
/// unit's `RestartPreventExitStatus=78` stops a five-second crash loop on a fault that will not heal.</para>
/// </remarks>
public sealed class TheKeywordListIsCheckedTests
{
    [Fact]
    public void AListThatParsedToNothingIsRefused() =>
        Program.WhyUnusable(SkeletonKeywords.From(string.Empty))
            .Should().Contain("no keyword list was parsed at all");

    /// <summary>A section with a heading and no words is the same accident, one level down.</summary>
    /// <remarks>
    /// This is the shape a half-written file actually takes — the heading survives an edit that
    /// removes the words under it — and it is the one a `Count == 0` check on the dictionary misses,
    /// because the dictionary has an entry.
    /// </remarks>
    [Fact]
    public void ALanguageWithNoWordsIsRefusedAndNamed()
    {
        var why = Program.WhyUnusable(SkeletonKeywords.From("[CSharp]\n# every line a comment\n"));

        why.Should().Contain("CSharp", "the operator has to know WHICH list is empty");
        why.Should().Contain("refused as if the contributor had leaked something");
    }

    /// <summary>The first empty language is named, even when a good one precedes it.</summary>
    [Fact]
    public void AGoodSectionDoesNotExcuseAnEmptyOneAfterIt() =>
        Program.WhyUnusable(SkeletonKeywords.From("[CSharp]\nlock\n\n[TypeScript]\n# nothing\n"))
            .Should().Contain("TypeScript");

    /// <summary>A usable list answers empty, which is what lets the server start.</summary>
    [Fact]
    public void AUsableListSaysNothing() =>
        Program.WhyUnusable(SkeletonKeywords.From("[CSharp]\nlock\nreturn\n"))
            .Should().BeEmpty();

    /// <summary>And the list this binary really carries passes its own check.</summary>
    /// <remarks>
    /// The one assertion here that would fail on a bad release rather than on a bad fixture: if the
    /// embedded resource is ever replaced by something that parses to nothing, this is red before
    /// the binary reaches a host.
    /// </remarks>
    [Fact]
    public void TheEmbeddedListPassesItsOwnCheck() =>
        Program.WhyUnusable(SkeletonKeywords.From(Program.Keywords()))
            .Should().BeEmpty("the list this binary carries is the one it will refuse to start without");
}
