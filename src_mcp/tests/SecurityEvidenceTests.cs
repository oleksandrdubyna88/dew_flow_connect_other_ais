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
        // A neutral path: nothing in "sample.cs" is an authz term, so only the REMOVED line can route.
        SecuritySignals.Classify([new("sample.cs", "+// removed")]).Single().Signals.Should().BeEmpty(
            "the control: the same file without the deleted line carries no signal");
        var files = SecuritySignals.Classify([new("sample.cs", "-[Authorize]\n+// removed")]);
        SecuritySignals.Triggered(new("redteam-auth", ["authz"], []), files).Should().BeTrue();
        SecuritySignals.Triggered(new("redteam-sql", ["sql"], []), files).Should().BeFalse();
    }

    [Fact]
    public void Credential_files_never_reach_the_prompt_even_when_their_text_matches_focus()
    {
        var files = SecuritySignals.Classify([new("server.pem", "PRIVATE KEY material"), new("Query.cs", "+ database.Query(value);")]);
        var pack = SecurityContext.Compose("Operator test instructions", new("redteam-sql", [], ["sql"]), files, "slice", new(24000));
        pack.Refusal.Should().BeEmpty();
        pack.Text.Should().Contain("database.Query").And.NotContain("PRIVATE KEY material");
        pack.Omitted.Should().Contain("server.pem");
    }

    [Fact]
    public void Detector_size_refusal_names_the_limit_instead_of_suggesting_more_context()
    {
        var files = SecuritySignals.Classify([new("Query.cs", new string('x', SecuritySignals.MaxFileCharacters + 1))]);
        var pack = SecurityContext.Compose("Operator test instructions", new("redteam-sql", ["sql"], []), files, "diff", new(24000));
        pack.Text.Should().BeEmpty();
        pack.Refusal.Should().Contain("detector character limit");
        pack.Omitted.Should().Contain("Query.cs (diff exceeds detector character limit)");
    }

    [Fact]
    public void A_patch_that_does_not_fit_does_not_hide_a_later_smaller_patch()
    {
        var files = SecuritySignals.Classify([new("A.cs", new string('\u754c', 30000)), new("B.cs", "+ query(value);")]);
        var pack = SecurityContext.Compose("Operator test instructions", new("redteam-sql", ["sql"], []), files, "diff", new(24000));
        pack.Refusal.Should().BeEmpty();
        pack.Omitted.Should().Contain("A.cs");
        pack.Text.Should().Contain("File: B.cs").And.Contain("query(value)").And.NotContain(new string('\u754c', 30));
    }

    [Fact]
    public void Production_source_precedes_signal_richer_supporting_material_under_a_tight_budget()
    {
        // The supporting file is CODE (prose is stripped of signals, so a .md fixture would lose on focus
        // alone), sorts before the production path, and carries more focus signals than it. Only the
        // production-before-supporting tier can put src/Orders.cs first; each file fills most of the budget.
        string[] focus = ["sql", "authz", "entry-point"];
        var files = SecuritySignals.Classify([
            new("docs/OrdersSample.cs", "+ [Authorize] endpoint database.Query(value);\n" + new string('x', 45000)),
            new("src/Orders.cs", "+ database.Query(value);\n" + new string('y', 45000)),
        ]);
        var supporting = files.Single(f => f.Diff.Path == "docs/OrdersSample.cs");
        var production = files.Single(f => f.Diff.Path == "src/Orders.cs");
        supporting.SupportingMaterial.Should().BeTrue();
        production.SupportingMaterial.Should().BeFalse();
        supporting.Signals.Intersect(focus).Count().Should().BeGreaterThan(production.Signals.Intersect(focus).Count(),
            "otherwise focus ranking alone would already put production first");
        var pack = SecurityContext.Compose("Operator test instructions", new("redteam-sql", ["sql"], focus),
            files, "slice", new(24000));
        pack.Refusal.Should().BeEmpty();
        pack.Text.Should().Contain("File: src/Orders.cs").And.NotContain("File: docs/OrdersSample.cs");
        pack.Omitted.Should().Contain(p => p.Contains("docs/OrdersSample.cs") && p.Contains("supporting material"));
    }
}
