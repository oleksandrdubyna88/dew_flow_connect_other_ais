using System.Collections.Immutable;
using Xunit;
using CoaiMcp.Core.Findings;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>One reviewer's JSON → one normalised review, whichever vendor produced it.</summary>
public sealed class ReviewParserTests
{
    private const string CodexShape = """
        {
          "findings": [{
            "severity": "major",
            "category": "security",
            "file": "src/Auth.cs",
            "line": 42,
            "title": "Token compared with ==",
            "why": "Non-constant-time comparison leaks length via timing",
            "fix": "Use CryptographicOperations.FixedTimeEquals"
          }]
        }
        """;

    [Fact]
    public void CodexShape_Normalises_FieldForField()
    {
        var outcome = ReviewParser.Parse(CodexShape, "codex");

        var review = outcome.Should().BeOfType<ParseOutcome.Success>().Subject.Review;
        review.Rejected.Should().BeEmpty();
        review.Findings.Should().ContainSingle().Which.Should().BeEquivalentTo(new Finding(
            Severity.Major,
            Category.Security,
            "src/Auth.cs",
            42,
            "Token compared with ==",
            "Non-constant-time comparison leaks length via timing",
            "Use CryptographicOperations.FixedTimeEquals",
            ImmutableArray.Create("codex")));
    }

    [Fact]
    public void GeminiShape_DifferentCasing_NormalisesTheSame()
    {
        // Gemini habitually capitalises keys; the contract is case-insensitive on purpose.
        var outcome = ReviewParser.Parse(
            """{ "Findings": [{ "Severity": "MINOR", "Category": "Ux", "Title": "t", "Why": "w", "Fix": "f" }] }""",
            "gemini");

        var review = outcome.Should().BeOfType<ParseOutcome.Success>().Subject.Review;
        var finding = review.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(Severity.Minor);
        finding.Category.Should().Be(Category.Ux);
        finding.Providers.Should().Equal("gemini");
    }

    [Fact]
    public void UnknownSeverity_RejectsThatFindingByName_OthersSurvive()
    {
        var outcome = ReviewParser.Parse(
            """
            { "findings": [
              { "severity": "critical", "category": "security", "title": "invented severity", "why": "w", "fix": "f" },
              { "severity": "nit", "category": "convention", "title": "fine", "why": "w", "fix": "f" }
            ]}
            """,
            "codex");

        var review = outcome.Should().BeOfType<ParseOutcome.Success>().Subject.Review;
        review.Findings.Should().ContainSingle().Which.Title.Should().Be("fine");
        review.Rejected.Should().ContainSingle().Which.Should().Match<RejectedEntry>(
            r => r.Index == 0 && r.Reason.Contains("'critical'"));
    }

    [Fact]
    public void MissingFileAndLine_SurvivesAsRepoLevelFinding()
    {
        // A plan-stage remark has nothing to point at; dropping it would un-gate the plan stage.
        var outcome = ReviewParser.Parse(
            """{ "findings": [{ "severity": "blocking", "category": "architecture", "title": "plan misses rollback", "why": "w", "fix": "f" }] }""",
            "gemini");

        var finding = outcome.Should().BeOfType<ParseOutcome.Success>().Subject.Review.Findings.Single();
        finding.File.Should().BeEmpty();
        finding.Line.Should().Be(0);
        finding.IsGating.Should().BeTrue();
    }

    [Fact]
    public void EmptyFindingsArray_IsAValidCleanReview()
    {
        var outcome = ReviewParser.Parse("""{ "findings": [] }""", "codex");

        outcome.Should().BeOfType<ParseOutcome.Success>()
            .Which.Review.Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"answer\": 42 }")]
    public void NotTheSchema_IsMalformed_WithAReason(string text) =>
        ReviewParser.Parse(text, "codex").Should().BeOfType<ParseOutcome.Malformed>()
            .Which.Reason.Should().NotBeEmpty();

    [Fact]
    public void SchemaFile_MatchesTheRecord()
    {
        // One copy of the contract: every enum value and every wire field is in the schema text.
        foreach (var value in new[] { "blocking", "major", "minor", "nit" })
        {
            FindingSchema.Json.Should().Contain($"\"{value}\"");
        }

        foreach (var value in new[] { "architecture", "security", "reliability", "performance", "ux", "convention" })
        {
            FindingSchema.Json.Should().Contain($"\"{value}\"");
        }

        foreach (var field in new[] { "severity", "category", "file", "line", "title", "why", "fix", "findings" })
        {
            FindingSchema.Json.Should().Contain($"\"{field}\"");
        }
    }
}
