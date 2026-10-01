using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityProtocolTests
{
    [Fact]
    public void Shipped_redteam_prompts_place_the_operator_boundary_before_output_rules()
    {
        foreach (var id in SecurityCatalog.Prompts.Select(p => p.Id).Append("redteam-general"))
        {
            var body = RolePrompts.ShippedDefaultFor(id);
            var boundary = body.IndexOf("### CONTEXT BOUNDARY & TARGET ISOLATION (CRITICAL):", StringComparison.Ordinal);
            var hygiene = body.IndexOf("### OUTPUT COMPLIANCE & JSON HYGIENE (ZERO-TOLERANCE):", StringComparison.Ordinal);
            boundary.Should().BeGreaterThanOrEqualTo(0, id);
            hygiene.Should().BeGreaterThan(boundary, id);
            body.LastIndexOf("\nIf no ", StringComparison.Ordinal).Should().BeGreaterThan(hygiene, id);
        }
    }

    [Fact]
    public void Only_fenced_material_follows_the_explicit_source_boundary()
    {
        const string body = "Operator instructions outside the source boundary.";
        var files = SecuritySignals.Classify([new("Sample.cs", "+ database.Query(value);")]);
        var pack = SecurityContext.Compose(body, new("redteam-sql", ["sql"], ["sql"]), files, "diff", 24000);
        const string start = "=== SOURCE CODE UNDER REVIEW (AUDIT ONLY BELOW) ===";
        const string end = "=== END OF SOURCE CODE ===";
        var opening = pack.Text.IndexOf(start, StringComparison.Ordinal);
        var closing = pack.Text.LastIndexOf(end, StringComparison.Ordinal);
        pack.Refusal.Should().BeEmpty();
        opening.Should().BeGreaterThan(pack.Text.IndexOf(SecuritySchema.Json, StringComparison.Ordinal));
        closing.Should().BeGreaterThan(opening);
        var source = pack.Text[opening..closing];
        source.Should().Contain("--- reviewed source (").And.Contain("--- end of reviewed source (")
            .And.Contain("File: Sample.cs").And.Contain("database.Query(value)").And.NotContain(body);
    }

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
    public void The_security_schema_does_not_offer_prose_that_its_validator_refuses()
    {
        using var security = JsonDocument.Parse(SecuritySchema.Json);
        security.RootElement.GetProperty("properties").TryGetProperty("notes", out _).Should().BeFalse();
        security.RootElement.GetProperty("required").EnumerateArray().Select(v => v.GetString())
            .Should().BeEquivalentTo(["findings", "status"]);
        using var ordinary = JsonDocument.Parse(FindingSchema.Json);
        ordinary.RootElement.GetProperty("properties").TryGetProperty("notes", out _).Should().BeTrue();
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
