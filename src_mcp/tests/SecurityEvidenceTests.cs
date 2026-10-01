using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityEvidenceTests
{
    private static Finding Defect(Severity severity) => new(severity, Category.Security, "Query.cs", 10,
        "Untrusted input reaches the query", "A caller changes query meaning", "Bind the value", ["ordinary"]);

    [Fact]
    public void Unreproduced_blocking_lane_finding_is_visible_but_cannot_gate()
    {
        var capped = SecurityEvidence.Attribute(Defect(Severity.Blocking), "local", "redteam-sql");
        capped.IsGating.Should().BeFalse();
        capped.CapReason.Should().NotBeEmpty();
        capped.AlsoSeenBy.Should().ContainSingle();
    }

    [Fact]
    public void Ordinary_ownership_preserves_the_stronger_reproduced_lane_severity()
    {
        var lane = SecurityEvidence.Attribute(Defect(Severity.Blocking) with
        { Reproduction = new("A test database", "Submit the recorded input", "No other rows", "Other rows returned") }, "local", "redteam-sql");
        var merged = SecurityEvidence.Merge([lane, Defect(Severity.Minor) with { Role = "Architecture" }]);
        merged.Should().ContainSingle();
        merged[0].Role.Should().Be("Architecture");
        merged[0].Severity.Should().Be(Severity.Blocking);
        merged[0].AlsoSeenBy[0].Reproduction.Should().NotBeNull();
    }

    [Fact]
    public void Stronger_lane_evidence_replaces_the_primary_cap_when_the_same_defect_merges()
    {
        var weak = SecurityEvidence.Attribute(Defect(Severity.Blocking), "local", "redteam-general");
        var strong = SecurityEvidence.Attribute(Defect(Severity.Blocking) with
        { Reproduction = new("Fixture database", "Submit fixture input", "Only owned rows", "Other rows returned") },
            "local", "redteam-sql");
        var merged = SecurityEvidence.Merge([weak, strong]).Single();
        merged.Severity.Should().Be(Severity.Blocking);
        merged.CapReason.Should().BeEmpty();
        merged.Reproduction.Should().Be(strong.Reproduction);
        merged.AlsoSeenBy.Should().HaveCount(2);
    }

    [Fact]
    public void Deleted_authorization_still_routes_the_security_prompt()
    {
        var files = SecuritySignals.Classify([new("Controller.cs", "-[Authorize]\n+// removed")]);
        SecuritySignals.Triggered(new("redteam-auth", ["authz"], []), files).Should().BeTrue();
        SecuritySignals.Triggered(new("redteam-sql", ["sql"], []), files).Should().BeFalse();
    }

    [Fact]
    public void Credential_files_never_reach_the_prompt_even_when_their_text_matches_focus()
    {
        var files = SecuritySignals.Classify([new("server.pem", "PRIVATE KEY material"), new("Query.cs", "+ database.Query(value);")]);
        var pack = SecurityContext.Compose("Operator test instructions", new("redteam-sql", [], ["sql"]), files, "slice", 24000);
        pack.Refusal.Should().BeEmpty();
        pack.Text.Should().Contain("database.Query").And.NotContain("PRIVATE KEY material");
        pack.Omitted.Should().Contain("server.pem");
    }
}
