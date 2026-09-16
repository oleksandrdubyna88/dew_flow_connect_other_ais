using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The word list is INSIDE the binary, and a deployed server can therefore start.
/// </summary>
/// <remarks>
/// <para><b>Four reviewers found the same thing in one round</b>, which is the shape of a defect
/// nobody argues about: the server read `shared/skeleton-keywords.txt` by walking parent directories
/// from its own location. That works in a checkout and nowhere a release runs — a published AOT
/// binary sits in a directory with no repository above it, so <c>Root()</c> threw
/// <c>FileNotFoundException</c> before the server listened. A build that succeeded produced a
/// deployment that could not start.</para>
/// <para>The list is an embedded resource now. This test is the thing that keeps it one: a renamed
/// or dropped <c>EmbeddedResource</c> in the csproj is a red test here rather than a server that
/// starts in CI and dies on the box.</para>
/// </remarks>
public sealed class TheKeywordsTravelInTheBinaryTests
{
    [Fact]
    public void TheListIsEmbeddedInTheAssembly() =>
        typeof(Corpus).Assembly.GetManifestResourceNames()
            .Should().Contain(
                Program.ResourceName,
                "a server that cannot find its word list cannot validate anything");

    /// <summary>And it is the real list, not an empty file that happens to be embedded.</summary>
    [Fact]
    public void TheEmbeddedListParsesIntoTheLanguagesTheCorpusHolds()
    {
        using var stream = typeof(Corpus).Assembly.GetManifestResourceStream(Program.ResourceName)!;
        using var text = new StreamReader(stream);

        var keywords = SkeletonKeywords.From(text.ReadToEnd());

        keywords.Keys.Should().Contain(["CSharp", "TypeScript", "JavaScript"]);
        keywords["CSharp"].Should().Contain(["class", "return", "lock"]);
        keywords["TypeScript"].Should().Contain(["const", "interface"]);
    }
}
