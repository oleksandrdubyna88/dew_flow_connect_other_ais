using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins the two small decisions on the lane's evidence path: which reviewer fields make an
/// <see cref="AttackEvidence"/> at all, and which findings get a stored security projection.
/// </summary>
/// <remarks>
/// Written against the code BEFORE their expressions were split to complexity 4 and observed green there
/// (todo/PLAN_security_lane_methods_within_complexity_4.md). Each field is varied ALONE, because the original
/// is one chain of <c>&amp;&amp;</c> and a dropped term would only show when that term is the one that differs.
/// </remarks>
public sealed class SecurityEvidenceProjectionCharacterizationTests
{
    [Fact]
    public void No_evidence_field_at_all_is_no_evidence() =>
        AttackEvidence.Read(null, null, null).Should().BeNull();

    [Theory]
    [InlineData(" trigger ", null, null, "trigger", "", "")]
    [InlineData(null, " mechanism ", null, "", "mechanism", "")]
    [InlineData(null, null, " consequence ", "", "", "consequence")]
    [InlineData("", null, null, "", "", "")]
    [InlineData("  ", "\tm\n", " c", "", "m", "c")]
    public void Any_one_field_makes_evidence_trimmed_with_the_missing_ones_empty(
        string? trigger, string? mechanism, string? consequence, string t, string m, string c) =>
        AttackEvidence.Read(trigger, mechanism, consequence).Should().Be(new AttackEvidence(t, m, c));

    private static Finding Ordinary() => new(Severity.Major, Category.Security, "A.cs", 3, "title", "why", "fix", ["qwen"]);

    [Fact]
    public void A_finding_with_no_security_part_projects_nothing()
    {
        SecurityFindingStore.Write(Ordinary()).Should().BeEmpty();
        SecurityFindingStore.Write(Ordinary() with { AlsoSeenBy = default }).Should().BeEmpty("a default array reads as empty");
    }

    public static TheoryData<string> Parts => ["reproduction", "attack evidence", "cap reason", "sighting"];

    private static Finding WithOnly(string part) => part switch
    {
        "reproduction" => Ordinary() with { Reproduction = new("p", "s", "e", "a") },
        "attack evidence" => Ordinary() with { AttackEvidence = new("t", "m", "c") },
        "cap reason" => Ordinary() with { CapReason = "Capped at minor." },
        _ => Ordinary() with { AlsoSeenBy = [new("qwen", "redteam-sql", Severity.Minor, null, string.Empty)] },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void Any_one_security_part_alone_is_projected_and_reads_back(string part)
    {
        var finding = WithOnly(part);
        var json = SecurityFindingStore.Write(finding);
        json.Should().Be(JsonSerializer.Serialize(SecurityFindingDetails.Of(finding), ServerJsonContext.Default.SecurityFindingDetails));
        var back = JsonSerializer.Deserialize(json, ServerJsonContext.Default.SecurityFindingDetails)!;
        back.Reproduction.Should().Be(finding.Reproduction);
        back.AttackEvidence.Should().Be(finding.AttackEvidence);
        back.CapReason.Should().Be(finding.CapReason);
        back.AlsoSeenBy.Should().HaveCount(finding.AlsoSeenBy.Length);
    }
}
