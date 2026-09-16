using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The server notices when its edge is misconfigured, instead of trusting a document.
/// </summary>
/// <remarks>
/// <para><b>The plan round's strongest theme, from two providers.</b> Everything that keeps a
/// contributor's address out of this system lives in `deploy/nginx/coai-bugs`: `access_log off`, no
/// `X-Real-IP`, no `X-Forwarded-For`. A reviewer put it plainly — the binary has no way to verify
/// the environment it runs in matches the contract it promises, so an operator who copies a stale
/// vhost, or whose location block inherits a distro `proxy_params`, gets a server that records
/// everybody and says nothing.</para>
/// <para>One reviewer proposed a mode that reads the nginx file and checks it. That is the wrong
/// instrument: this process cannot know where that file is, `include` and templating mean the file
/// on disk is not the effective config, and a check that passes on a file nginx never loaded is
/// worse than no check at all. <b>The request itself is the evidence.</b> If a forwarding header
/// arrives, the edge is sending one — whatever any file says — and that is a fact this process can
/// observe directly.</para>
/// <para><b>It reports the NAME and never the value.</b> A warning that quoted the address would be
/// the very leak it exists to report, written by the code that noticed it.</para>
/// </remarks>
public sealed class TheEdgeIsWatchedTests
{
    private static HttpRequest With(string header, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[header] = value;

        return context.Request;
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Real-IP")]
    [InlineData("Forwarded")]
    [InlineData("CF-Connecting-IP")]
    [InlineData("True-Client-IP")]
    public void AForwardingHeaderIsNoticed(string header) =>
        Program.EdgeSentAnAddress(With(header, "203.0.113.7"))
            .Should().Be(header, "the edge is sending an address, whatever the vhost on disk says");

    /// <summary>The name is the report. The value is the thing being protected.</summary>
    [Fact]
    public void TheAddressItselfIsNeverPartOfTheReport() =>
        Program.EdgeSentAnAddress(With("X-Forwarded-For", "203.0.113.7"))
            .Should().NotContain("203.0.113.7", "a warning quoting the address IS the leak");

    /// <summary>An ordinary request says nothing, so the warning means something when it appears.</summary>
    [Fact]
    public void ARequestFromACorrectEdgeIsSilent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer something";

        Program.EdgeSentAnAddress(context.Request).Should().BeEmpty();
    }

    /// <summary>
    /// A keyword list that parsed to nothing is a server that refuses every pair, politely.
    /// </summary>
    /// <remarks>
    /// `/health` answers from a route that touches neither the list nor the database, so a build
    /// accident that embedded an empty file would start, answer the smoke, publish, and then refuse
    /// every submission with "this server has no keyword list for CSharp" — which reads like the
    /// contributor's fault. (Plan round, local.)
    /// </remarks>
    [Fact]
    public void AnEmptyKeywordListStopsTheServerStarting()
    {
        var act = () => Program.Checked(SkeletonKeywords.From(string.Empty));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no keyword list*");
    }

    /// <summary>And a section that parsed to no words is the same accident, one level down.</summary>
    [Fact]
    public void ALanguageWithNoWordsStopsItToo()
    {
        var act = () => Program.Checked(SkeletonKeywords.From("[CSharp]\n# every line a comment\n"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CSharp*");
    }

    [Fact]
    public void TheRealEmbeddedListPasses()
    {
        using var stream = typeof(Corpus).Assembly.GetManifestResourceStream(Program.ResourceName)!;
        using var text = new StreamReader(stream);

        var act = () => Program.Checked(SkeletonKeywords.From(text.ReadToEnd()));

        act.Should().NotThrow("the list this binary actually carries must satisfy its own check");
    }
}
