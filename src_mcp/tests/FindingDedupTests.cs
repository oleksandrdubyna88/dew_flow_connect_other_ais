using System.Collections.Immutable;
using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class FindingDedupTests
{
    private static Finding F(
        string provider,
        string file = "src/Auth.cs",
        int line = 40,
        Category category = Category.Security,
        Severity severity = Severity.Major,
        string title = "token compared with ==") =>
        new(severity, category, file, line, title, "why", "fix", [provider]);

    [Fact]
    public void SameDefectFromTwoProviders_CountsOnce_ListsBothProviders()
    {
        var merged = FindingDedup.Merge([
            F("codex", line: 40, title: "token compared with =="),
            F("gemini", line: 42, title: "Token is compared with =="),
        ]);

        merged.Should().ContainSingle().Which.Providers.Should().BeEquivalentTo("codex", "gemini");
    }

    [Fact]
    public void SameFileDifferentCategory_StaysTwoFindings() =>
        FindingDedup.Merge([
            F("codex", category: Category.Security),
            F("gemini", category: Category.Performance),
        ]).Should().HaveCount(2);

    [Fact]
    public void LinesFiveApart_Merge_SixApart_DoNot()
    {
        FindingDedup.Merge([F("codex", line: 40), F("gemini", line: 45)]).Should().HaveCount(1);
        FindingDedup.Merge([F("codex", line: 40), F("gemini", line: 46)]).Should().HaveCount(2);
    }

    [Fact]
    public void DifferentRemarks_OnTheSameLine_StayTwoFindings() =>
        FindingDedup.Merge([
            F("codex", title: "token compared with =="),
            F("gemini", title: "missing cancellation token on the request path"),
        ]).Should().HaveCount(2);

    [Fact]
    public void SeverityDisagreement_ResolvesTowardCaution()
    {
        var merged = FindingDedup.Merge([
            F("codex", severity: Severity.Major),
            F("gemini", severity: Severity.Blocking),
        ]);

        merged.Should().ContainSingle().Which.Severity.Should().Be(Severity.Blocking);
    }

    [Fact]
    public void WindowsAndUnixPaths_AreTheSameFile() =>
        FindingDedup.Merge([
            F("codex", file: "src/Auth.cs"),
            F("gemini", file: "src\\Auth.cs"),
        ]).Should().HaveCount(1);
}
