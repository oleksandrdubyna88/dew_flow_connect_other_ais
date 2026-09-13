using CoaiMcp.Core.Findings;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A document reviewer's answer survives the wire — and its summary reaches the caller.
/// </summary>
/// <remarks>
/// <para>This is the defect that would have shipped a green build over a broken feature. The six
/// categories are about CODE, and <see cref="ReviewParser"/> turns anything else into a
/// <see cref="RejectedEntry"/> — so a requirements reviewer answering <c>completeness</c> had every
/// finding dropped, while codex, whose schema the API enforces, would have been refused outright.
/// Nothing about that is visible from the roles page.</para>
/// <para><c>notes</c> is the other half of what the manager asked for. A round returns findings and a
/// verdict; a summary is neither, and there was no channel for one.</para>
/// </remarks>
public sealed class ADocumentFindingFitsTheWireTests
{
    private static string OneFinding(string category) => $$"""
        {"findings": [
          {"severity": "major", "category": "{{category}}", "file": null, "line": null,
           "title": "the acceptance criteria are not testable",
           "why": "nothing says what 'fast' means", "fix": "name a number"}
        ]}
        """;

    private static NormalisedReview Parsed(string json) =>
        ReviewParser.Parse(json, "codex").Should().BeOfType<ParseOutcome.Success>().Subject.Review;

    [Theory]
    [InlineData("clarity", Category.Clarity)]
    [InlineData("completeness", Category.Completeness)]
    [InlineData("consistency", Category.Consistency)]
    [InlineData("feasibility", Category.Feasibility)]
    public void ADocumentCategory_BecomesAFinding(string said, Category expected)
    {
        var review = Parsed(OneFinding(said));

        review.Rejected.Should().BeEmpty("a document reviewer answered a document category");
        review.Findings.Should().ContainSingle().Which.Category.Should().Be(expected);
    }

    /// <summary>The regression guard: widening the list must not disturb what was already in it.</summary>
    [Theory]
    [InlineData("architecture", Category.Architecture)]
    [InlineData("security", Category.Security)]
    [InlineData("reliability", Category.Reliability)]
    [InlineData("performance", Category.Performance)]
    [InlineData("ux", Category.Ux)]
    [InlineData("convention", Category.Convention)]
    public void ACodeCategory_StillBecomesAFinding(string said, Category expected) =>
        Parsed(OneFinding(said)).Findings.Should().ContainSingle().Which.Category.Should().Be(expected);

    /// <summary>
    /// Still NAMED, never dropped. Widening a list is how a list stops refusing anything, and the
    /// value of the refusal is that a caller can see what a vendor invented.
    /// </summary>
    [Fact]
    public void AnInventedCategory_IsStillRejectedByName()
    {
        var review = Parsed(OneFinding("tone"));

        review.Findings.Should().BeEmpty();
        review.Rejected.Should().ContainSingle().Which.Reason.Should().Contain("tone");
    }

    /// <summary>The schema is what codex is handed; the enum is what this program holds.</summary>
    [Fact]
    public void TheSchema_OffersExactlyTheCategoriesTheParserTakes()
    {
        foreach (var category in Enum.GetNames<Category>())
        {
            FindingSchema.Json.Should().Contain($"\"{category.ToLowerInvariant()}\"",
                $"a reviewer cannot answer {category} unless the schema offers it");
        }
    }

    [Fact]
    public void AReviewWithNotes_CarriesThem()
    {
        var review = Parsed("""
            {"findings": [], "notes": "The document proposes a two-phase rollout and never says who signs off."}
            """);

        review.Findings.Should().BeEmpty();
        review.Notes.Should().StartWith("The document proposes");
    }

    /// <summary>
    /// A code round asks for no notes and gets none. Absent, null and empty are the same successful
    /// review, and none of them is a failure anybody is told about.
    /// </summary>
    [Theory]
    [InlineData("""{"findings": []}""")]
    [InlineData("""{"findings": [], "notes": null}""")]
    [InlineData("""{"findings": [], "notes": "   "}""")]
    public void AReviewWithoutNotes_IsAnOrdinaryReview(string json)
    {
        var review = Parsed(json);

        review.Notes.Should().BeEmpty();
        review.Rejected.Should().BeEmpty();
    }

    /// <summary>
    /// The load-bearing one: prose has no severity and must never acquire one. A round whose whole
    /// content is a summary gates nothing.
    /// </summary>
    [Fact]
    public void NotesCannotGateARound()
    {
        var review = Parsed("""{"findings": [], "notes": "Blocking: this is a serious problem."}""");

        review.Findings.Should().BeEmpty("prose is not a finding, whatever words are in it");
        review.Findings.Count(f => f.IsGating).Should().Be(0);
    }

    /// <summary>
    /// OpenAI's structured-output rules are not JSON Schema's: optionality is a nullable TYPE, never
    /// an absent requirement. Learned from a 400 on every reviewer on 2026-08-31.
    /// </summary>
    [Fact]
    public void TheSchemaDeclaresNotes_AsRequiredAndNullable()
    {
        FindingSchema.Json.Should().Contain("\"required\": [\"findings\", \"notes\"]");
        FindingSchema.Json.Should().Contain("\"notes\": { \"type\": [\"string\", \"null\"]");
    }
}
