using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityProtocolTests
{
    private const string Finding = """
        {"severity":"major","category":"security","file":"Controller.cs","line":10,
        "title":"Fixture tenant boundary regression","why":"Fixture evidence","fix":"Restore tenant scope",
        "trigger":"Read endpoint with another tenant's id","mechanism":"Tenant filter removed",
        "consequence":"Another tenant's row is returned","reproduction":null}
        """;

    private static ReviewerOutcome Check(string json)
    {
        var review = ReviewParser.Parse(json, "qwen").Should().BeOfType<ParseOutcome.Success>().Subject.Review;
        var work = new ReviewerWork(new("qwen", "redteam-authz", new ProcessRequest("fixture", [], ".")))
        { IsSecurity = true };
        return SecurityAnswerLimit.Apply(work, new ReviewerOutcome.Ok(review, false));
    }

    [Fact]
    public void A_security_answer_requires_its_status() =>
        Check("""{"findings":[]}""").Should().BeOfType<ReviewerOutcome.Unparseable>();

    [Fact]
    public void Secure_does_not_carry_prose_remarks() =>
        Check("""{"status":"SECURE","findings":[],"notes":"Consider a hypothetical improvement."}""")
            .Should().BeOfType<ReviewerOutcome.Unparseable>();

    [Fact]
    public void Secure_cannot_contradict_a_finding() =>
        Check("{\"status\":\"SECURE\",\"findings\":[" + Finding + "]}")
            .Should().BeOfType<ReviewerOutcome.Unparseable>();

    [Fact]
    public void Missing_mechanism_cannot_count_as_an_answer() =>
        Check("{\"status\":\"FINDINGS\",\"findings\":[" + Finding.Replace("Tenant filter removed", "") + "]}")
            .Should().BeOfType<ReviewerOutcome.Unparseable>();

    [Fact]
    public void The_schema_requires_status_and_the_three_evidence_fields()
    {
        using var schema = JsonDocument.Parse(SecuritySchema.Json);
        schema.RootElement.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Should().Contain("status");
        schema.RootElement.GetProperty("properties").GetProperty("findings").GetProperty("items")
            .GetProperty("required").EnumerateArray().Select(v => v.GetString())
            .Should().Contain(["trigger", "mechanism", "consequence"]);
    }

    [Fact]
    public void Required_evidence_survives_the_real_parser_and_lane_attribution()
    {
        var answer = Check("{\"status\":\"FINDINGS\",\"findings\":[" + Finding + "]}")
            .Should().BeOfType<ReviewerOutcome.Ok>().Subject;
        var normalized = answer.Review.Findings.Single();
        normalized.AttackEvidence!.Mechanism.Should().Be("Tenant filter removed");
        SecurityEvidence.Attribute(normalized, "qwen", "redteam-authz").AlsoSeenBy.Single()
            .AttackEvidence.Should().Be(normalized.AttackEvidence);
    }

    [Theory]
    [InlineData("SECURE", false)]
    [InlineData("FINDINGS", true)]
    public void A_consistent_status_and_complete_evidence_are_accepted(string status, bool hasFinding) =>
        Check("{\"status\":\"" + status + "\",\"findings\":[" + (hasFinding ? Finding : "") + "]}")
            .Should().BeOfType<ReviewerOutcome.Ok>();
}
