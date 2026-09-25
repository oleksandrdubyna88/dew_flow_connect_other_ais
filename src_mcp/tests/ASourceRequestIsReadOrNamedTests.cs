using CoaiMcp.Core.Findings;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A feature reviewer's request for code is read into the review — or refused by name, never a crash.
/// </summary>
/// <remarks>
/// <para>The request names a FILE, and the file will be read out of git by a later story (the source
/// resolver, plan §4.9). A path that climbs out of the repository, or names a drive, is refused here —
/// at the parser, as a sentence, beside the requests that were fine — so it can never cost a process
/// or reach a reader. Same discipline as an unknown severity: the entry is named, and its neighbours
/// survive it.</para>
/// </remarks>
public sealed class ASourceRequestIsReadOrNamedTests
{
    private static NormalisedReview Parsed(string json) =>
        ReviewParser.Parse(json, "grok").Should().BeOfType<ParseOutcome.Success>().Subject.Review;

    [Fact]
    public void RequestsAreRead_WithAbsentPartsAsEmpty()
    {
        var review = Parsed(
            """
            {"findings": [], "notes": null, "sourceRequests": [
              {"file": "src/Cart.cs", "symbol": "Add", "startLine": null, "endLine": null, "why": "the seam"},
              {"file": "src/Store.cs", "symbol": null, "startLine": 10, "endLine": 40, "why": "the lock"}
            ]}
            """);

        review.SourceRequests.Should().Equal(
            new SourceRequest("src/Cart.cs", "Add", 0, 0, "the seam"),
            new SourceRequest("src/Store.cs", string.Empty, 10, 40, "the lock"));
        review.RejectedSourceRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"findings": []}""")]
    [InlineData("""{"findings": [], "sourceRequests": null}""")]
    [InlineData("""{"findings": [], "sourceRequests": []}""")]
    public void NoRequests_IsEmpty_NeverNull(string json)
    {
        var review = Parsed(json);

        review.SourceRequests.IsDefault.Should().BeFalse("empty, never null — a caller must not have to ask");
        review.SourceRequests.Should().BeEmpty();
        review.RejectedSourceRequests.IsDefault.Should().BeFalse();
        review.RejectedSourceRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../secrets.txt", "climbs out")]
    [InlineData("src/../../etc/passwd", "climbs out")]
    [InlineData("/etc/passwd", "absolute")]
    [InlineData("\\\\server\\share\\x.cs", "absolute")]
    [InlineData("C:/Windows/win.ini", "drive")]
    [InlineData("", "names no file")]
    public void APathOutsideTheRepository_IsANamedRejection(string file, string reason)
    {
        var json = $$"""
            {"findings": [{"severity": "major", "category": "architecture", "file": null, "line": null,
                           "title": "t", "why": "w", "fix": "f"}],
             "sourceRequests": [
               {"file": "src/Ok.cs", "symbol": null, "startLine": null, "endLine": null, "why": "fine"},
               {"file": {{System.Text.Json.JsonSerializer.Serialize(file)}}, "symbol": null, "startLine": null, "endLine": null, "why": "w"}
             ]}
            """;

        var review = Parsed(json);

        review.Findings.Should().ContainSingle("a bad request takes nothing else down with it");
        review.SourceRequests.Select(request => request.File).Should().Equal("src/Ok.cs");
        var rejected = review.RejectedSourceRequests.Should().ContainSingle().Subject;
        rejected.Index.Should().Be(1);
        rejected.Reason.Should().Contain(reason);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(20, 10)]
    [InlineData(-3, null)]
    public void LinesThatNameNoSpan_AreANamedRejection(int? start, int? end)
    {
        var json = $$"""
            {"findings": [], "sourceRequests": [
              {"file": "src/A.cs", "symbol": null, "startLine": {{start?.ToString() ?? "null"}}, "endLine": {{end?.ToString() ?? "null"}}, "why": "w"}
            ]}
            """;

        var review = Parsed(json);

        review.SourceRequests.Should().BeEmpty();
        review.RejectedSourceRequests.Single().Reason.Should().Contain("line");
    }

    [Fact]
    public void ARequestWithoutAFileField_IsNamed_NotACrash()
    {
        var review = Parsed("""{"findings": [], "sourceRequests": [{"why": "w"}, null]}""");

        review.SourceRequests.Should().BeEmpty();
        review.RejectedSourceRequests.Select(entry => entry.Index).Should().Equal(0, 1);
    }
}
