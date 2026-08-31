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
    public void TheSameDefectInDifferentWords_Merges_WhenFileAndLineAnchorIt()
    {
        // Verbatim from the real run of 2026-08-31: two reviewers, one path-traversal defect at
        // Store.cs:10, 0.43 similar by title — counted twice under a single strict threshold.
        var merged = FindingDedup.Merge([
            F("codex", file: "src/Store.cs", line: 10,
              title: "Unvalidated paste IDs can escape the configured storage root"),
            F("gemini", file: "src/Store.cs", line: 10,
              title: "Unvalidated paste IDs allow writes and reads outside the configured root"),
        ]);

        merged.Should().ContainSingle("a gate whose count grows with the number of reviewers is the bug dedup exists to prevent")
            .Which.Providers.Should().BeEquivalentTo("codex", "gemini");
    }

    /// <summary>
    /// The limit of a lexical rule, recorded rather than tuned away.
    /// </summary>
    /// <remarks>
    /// Also verbatim from the real run: two roles found ONE quadratic scan and shared almost no
    /// vocabulary describing it — "Search rescans and rereads the entire store once for every
    /// paste" against "Search performs quadratic directory scans and file reads", 0.12 similar.
    /// Lowering the threshold far enough to merge these would also merge the genuinely different
    /// remarks in <see cref="UnrelatedRemarksOnTheSameLine_StillDoNotMerge"/> (0.20). So this pair
    /// counts twice, and the honest cure is a semantic comparison rather than a smaller number —
    /// which is a change worth making deliberately, not by moving a constant until a test passes.
    /// </remarks>
    [Fact]
    public void TwoWordingsWithNoSharedVocabulary_StillCountTwice_AndThatIsTheKnownLimit()
    {
        FindingDedup.Merge([
            F("codex", file: "src/Store.cs", line: 37, category: Category.Performance,
              title: "Search rescans and rereads the entire store once for every paste"),
            F("codex", file: "src/Store.cs", line: 39, category: Category.Performance,
              title: "Search performs quadratic directory scans and file reads"),
        ]).Should().HaveCount(2);
    }

    [Fact]
    public void UnrelatedRemarksOnTheSameLine_StillDoNotMerge()
    {
        // The looser threshold must not become "anything on one line is one finding".
        FindingDedup.Merge([
            F("codex", file: "src/A.cs", line: 20, category: Category.Reliability,
              title: "the cancellation token is never observed"),
            F("gemini", file: "src/A.cs", line: 22, category: Category.Reliability,
              title: "the returned stream is left undisposed"),
        ]).Should().HaveCount(2);
    }

    [Fact]
    public void RepoLevelFindings_StillNeedRealSimilarity()
    {
        // No file, no line: the title is all there is, so the strict threshold stands.
        FindingDedup.Merge([
            F("codex", file: "", line: 0, category: Category.Architecture, title: "the plan has no rollback step"),
            F("gemini", file: "", line: 0, category: Category.Architecture, title: "the build order contradicts the stated scope"),
        ]).Should().HaveCount(2);
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
