using System.Text;
using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Prints what the default rules budget (90 000 characters until 2026-11-06, 93 301 since) holds, through the PRODUCTION collector — explicit, never part of the normal
/// suite (results in <c>research/RESULTS_rules_selection_budget.md</c>, section 2026-11-01).
/// </summary>
/// <remarks>
/// <para>Run it with <c>COAI_RULE_BUDGET_OUT=&lt;file&gt; ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --explicit only
/// --filter-class CoaiMcp.Tests.RuleBudgetMeasurementTests</c>. The table goes to that file and to standard output. It asserts
/// nothing about the numbers: the canaries in <c>StageRulesTests</c> pin them, and this reproduces them.</para>
/// <para>The population is the one <c>StageRulesTests</c> documents: <c>feat/branch-&lt;i&gt;</c> for even i and
/// <c>fix/issue-&lt;i&gt;-thing</c> for odd i, i in 0..99, and the same shape for i in 0..2999. The corpus measured is the
/// conventions mount of the checkout the test runs from.</para>
/// </remarks>
public sealed class RuleBudgetMeasurementTests
{
    private const string TestLeftovers = "common/test-leftovers.md";

    [Fact(Explicit = true)]
    public void The_table_the_results_file_quotes_is_printed_from_the_production_collector()
    {
        var output = Environment.GetEnvironmentVariable("COAI_RULE_BUDGET_OUT");
        output.Should().NotBeNullOrWhiteSpace("set COAI_RULE_BUDGET_OUT to the file the table is written to");

        var root = RepositoryRootWithAMount();
        RuleBundle Collect(string branch, int budget = RuleFiles.DefaultBudgetBytes) =>
            RuleFiles.Collect(root, budget, RuleOrder.ForBranch(branch));
        static IEnumerable<string> Mounted(RuleBundle bundle) =>
            bundle.Files.Where(file => !file.Path.Equals(file.WithinMount, StringComparison.Ordinal)).Select(file => file.WithinMount);
        List<string> TailOf(string branch) => [.. Mounted(Collect(branch)).Where(within => !RuleOrder.IsTier(within))];
        static string Branch(int i) => i % 2 == 0 ? $"feat/branch-{i}" : $"fix/issue-{i}-thing";

        var table = new StringBuilder();

        var everything = Collect("canary/the-tail", budget: 10_000_000);
        var baseChars = everything.Files.Where(file => file.Path.Equals(file.WithinMount, StringComparison.Ordinal)).Sum(file => file.Text.Length);
        var tierChars = everything.Files
            .Where(file => !file.Path.Equals(file.WithinMount, StringComparison.Ordinal) && RuleOrder.IsTier(file.WithinMount))
            .Sum(file => file.Text.Length);
        table.AppendLine($"base={baseChars} tier={tierChars} base+tier={baseChars + tierChars} free={RuleFiles.DefaultBudgetBytes - baseChars - tierChars}");

        table.AppendLine("rules outside the tier, by size (characters):");
        foreach (var file in everything.Files
                     .Where(file => !file.Path.Equals(file.WithinMount, StringComparison.Ordinal) && !RuleOrder.IsTier(file.WithinMount))
                     .OrderBy(file => file.Text.Length))
        {
            table.AppendLine($"  {file.Text.Length,6} {file.WithinMount}");
        }

        var canary = Collect("canary/the-tail");
        table.AppendLine($"canary tail: {string.Join(", ", Mounted(canary).Where(within => !RuleOrder.IsTier(within)))} (shown: {canary.Files.Count} files, {canary.Bytes} characters)");

        var hundred = Enumerable.Range(0, 100).Select(Branch).Select(branch => (Branch: branch, Tail: TailOf(branch))).ToList();
        foreach (var (branch, tail) in hundred)
        {
            table.AppendLine($"population {branch} draws={tail.Contains(TestLeftovers)} n={tail.Count} [{string.Join(",", tail)}]");
        }

        var widest = hundred.MaxBy(entry => entry.Tail.Count);
        var narrowest = hundred.MinBy(entry => entry.Tail.Count);
        table.AppendLine($"share over 100: {hundred.Count(entry => entry.Tail.Contains(TestLeftovers))}/100, widest {widest.Tail.Count} ({widest.Branch}), narrowest {narrowest.Tail.Count} ({narrowest.Branch})");

        var sizes = new int[8];
        var drawn = 0;
        for (var i = 0; i < 3000; i++)
        {
            var tail = TailOf(Branch(i));
            sizes[tail.Count]++;
            drawn += tail.Contains(TestLeftovers) ? 1 : 0;
        }

        table.AppendLine($"over 3000: {TestLeftovers} drawn on {drawn}; tails of size 0..7 = [{string.Join(",", sizes)}]");
        table.AppendLine($"omitted on the canary branch: {string.Join(" | ", canary.Omitted)}");

        File.WriteAllText(output!, table.ToString());
        Console.Write(table.ToString());
    }

    private static string RepositoryRootWithAMount()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".agents", "conventions", "common")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("no .agents/conventions/common above the test binary: git submodule update --init .agents/conventions");
    }
}
