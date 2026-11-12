using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The C# guard over THIS repository (E5 S6, <c>todo/PLAN_test_leftovers_E5_S6_brief.md</c> § 7.1). The template's file of this name holds one assertion; this one is coai's own (plan D2): after the guard, the
/// scan's own findings (no baseline: the <c>raw-temp</c> baseline was drained and deleted by E5 S7-d, so any raw temp call outside the helpers is red), the token rows and the expected-exemption inventory (<see cref="LeftoversInventory"/>). It needs <c>node</c> and the mounted conventions;
/// a missing one is red with the guard's own sentence. It never skips.
/// </summary>
public sealed class LeftoversRepositoryGuardTests
{
    /// <summary>Every root the C# guard reads in this repository. A root with no file is a discovery that broke.</summary>
    private static readonly string[] Roots =
    [
        "src_mcp/tests/", "src_mcp/tests_leftovers/", "src_mcp/tests_fakecli/", "src_server/tests/", "src_bugs/tests/", "src_bench/CoaiBench.Tests/",
        ".github/workflows/", ".github/scripts/", "http/", "src_vs_code/scripts/",
    ];

    [Fact]
    public void This_repository_passes_the_guard()
    {
        var root = RepositoryRoot.Path;

        var report = LeftoversGuard.Scan(root, root);

        report.FilesScanned.Should().BeGreaterThan(0, report.Describe(root));
        var discovered = GuardFiles.Discover(root).Select(file => file.Relative).ToList();
        foreach (var scanned in Roots)
        {
            discovered.Should().Contain(file => file.StartsWith(scanned, StringComparison.Ordinal), $"scanned nothing under {scanned} — discovery broke; the repository is not clean");
        }

        // One scope: every failure is reported, so a mount that is not initialised shows the token sentence (with its cure) beside the classifier's, not only the first of them.
        using var scope = new AssertionScope();
        var left = report.Findings;
        left.Should().BeEmpty(Environment.NewLine + string.Join(Environment.NewLine, left.Select(f => f.ToString())));
        LeftoversGuard.RegisteredTokenFindings(root, OwnedToken.RepoToken, OwnedToken.RepoRow).Should().BeEmpty();
        LeftoversGuard.ConstantFindings(root, OwnedToken.RepoToken).Should().BeEmpty();
        InventoryDifferences(root).Should().BeEmpty("shared/leftovers-exemptions.json is updated in the same change as a marker or its site");
    }

    /// <summary>The inventory's differences; a <c>node</c> that cannot answer is one difference naming the cause, never an exception that hides the other findings.</summary>
    private static IReadOnlyList<string> InventoryDifferences(string root)
    {
        try
        {
            return LeftoversInventory.Differences(root);
        }
        catch (InvalidOperationException ex)
        {
            return [ex.Message];
        }
    }
}
