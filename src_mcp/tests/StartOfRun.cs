namespace CoaiMcp.Tests;

/// <summary>
/// What the test run does about the machine's old <c>coai-*</c> folders at its start: it COUNTS them and says so — the ONE entry point the
/// assembly fixture calls (<c>todo/PLAN_test_leftovers_E5_S2_spec.md</c>, E5 S2-A).
/// </summary>
/// <remarks>
/// <para>Since E5 S2-A nothing in the test start takes a folder: the age-only <c>coai-</c> sweep that stood here is retired, and a count reads NAMES ONLY — no age, no
/// stat, no claim. Whether a name is a person's chat, the product's round or a dead run's folder is not a question a name can answer; the one-off clean-up
/// (<c>scripts/old-coai-leftovers.mjs</c>) lists them with their ages and claims and takes only what a person picks.</para>
/// <para>E5 S2-B retired the second count, of the legacy <c>testsToDelete-coai-run-*</c> run folders: the owner-aware sweep (<c>LeftoversSweepFixture</c>) now LISTS each one by name with its
/// <c>rm</c> command, which a count beside it would only repeat more weakly. The <c>coai-*</c> count stays: the sweep never looks at those names.</para>
/// <para>This file references nothing of the product's server namespace: a test start-of-run that cannot reach the product's sweeper cannot call it.</para>
/// </remarks>
internal static class StartOfRun
{
    /// <summary>The old <c>coai-*</c> directories in <paramref name="tempRoot"/>: not a version cache (<c>coai-0.17.5</c>), not under a product prefix of <paramref name="rule"/>.</summary>
    internal static int Run(string tempRoot, SweepRule rule) =>
        Count(tempRoot, "coai-*", name => IsOldTestName(name, rule));

    internal static string Sentence(int count, string tempRoot) =>
        count == 0
            ? $"run: no old coai-* leftovers in {tempRoot}"
            : $"run: {count} old coai-* director{(count == 1 ? "y" : "ies")} in {tempRoot} — no test sweep takes them (E5 S2-A); see research/module_tests.md § The one-off clean-up";

    private static bool IsOldTestName(string name, SweepRule rule) =>
        name.StartsWith("coai-", StringComparison.Ordinal)
        && !(name.Length > "coai-".Length && name["coai-".Length] is >= '0' and <= '9')
        && !rule.NeverSwept.Any(owned => name.StartsWith(owned, StringComparison.Ordinal));

    private static int Count(string tempRoot, string pattern, Func<string, bool> named)
    {
        try
        {
            return new DirectoryInfo(tempRoot).EnumerateDirectories(pattern).Count(d => d.LinkTarget is null && named(d.Name));
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }
}
