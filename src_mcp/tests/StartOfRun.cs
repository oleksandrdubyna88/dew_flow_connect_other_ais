using System.Text.RegularExpressions;

namespace CoaiMcp.Tests;

/// <summary>
/// What the test run does about the machine's old <c>coai-*</c> folders at its start: it COUNTS them and says so — the ONE entry point the
/// assembly fixture calls (<c>todo/PLAN_test_leftovers_E5_S2_spec.md</c>, E5 S2-A).
/// </summary>
/// <remarks>
/// <para>Since E5 S2-A nothing in the test start takes a folder: the age-only <c>coai-</c> sweep that stood here is retired, and a count reads NAMES ONLY — no age, no
/// stat, no claim. Whether a name is a person's chat, the product's round or a dead run's folder is not a question a name can answer; the one-off clean-up
/// (<c>scripts/old-coai-leftovers.mjs</c>) lists them with their ages and claims and takes only what a person picks.</para>
/// <para>This file references nothing of the product's server namespace: a test start-of-run that cannot reach the product's sweeper cannot call it.</para>
/// </remarks>
internal static class StartOfRun
{
    /// <summary>The folder a run makes and ends itself — a legacy name of the rule (its second segment is no owner), left behind by a killed run.</summary>
    private static readonly Regex LegacyRunFolder = new(@"^testsToDelete-coai-run-[0-9a-f]{6}$", RegexOptions.CultureInvariant);

    /// <summary>The old <c>coai-*</c> directories in <paramref name="tempRoot"/>: not a version cache (<c>coai-0.17.5</c>), not under a product prefix of <paramref name="rule"/>.</summary>
    internal static int Run(string tempRoot, SweepRule rule) =>
        Count(tempRoot, "coai-*", name => IsOldTestName(name, rule));

    /// <summary>
    /// The legacy <c>testsToDelete-coai-run-&lt;6 hex&gt;</c> directories in <paramref name="tempRoot"/> EXCEPT <paramref name="ownFolder"/> — this run's own folder is made
    /// before the fixture counts, and a count that included it would read one on a clean machine. A name cannot tell a dead run's folder from another run's live one.
    /// </summary>
    internal static int RunFolders(string tempRoot, string ownFolder) =>
        Count(tempRoot, "testsToDelete-coai-run-*", name => LegacyRunFolder.IsMatch(name), ownFolder);

    internal static string Sentence(int count, string tempRoot) =>
        count == 0
            ? $"run: no old coai-* leftovers in {tempRoot}"
            : $"run: {count} old coai-* director{(count == 1 ? "y" : "ies")} in {tempRoot} — no test sweep takes them (E5 S2-A); see research/module_tests.md § The one-off clean-up";

    internal static string LegacySentence(int count, string tempRoot) =>
        $"run: {count} legacy testsToDelete-coai-run-* director{(count == 1 ? "y" : "ies")} of other runs in {tempRoot} — a killed run leaves one; some may belong to a run still going; "
        + "the one-off clean-up lists them with their claim (E5 S2-A); see research/module_tests.md § The one-off clean-up";

    private static bool IsOldTestName(string name, SweepRule rule) =>
        name.StartsWith("coai-", StringComparison.Ordinal)
        && !(name.Length > "coai-".Length && name["coai-".Length] is >= '0' and <= '9')
        && !rule.NeverSwept.Any(owned => name.StartsWith(owned, StringComparison.Ordinal));

    private static int Count(string tempRoot, string pattern, Func<string, bool> named, string? except = null)
    {
        try
        {
            return new DirectoryInfo(tempRoot).EnumerateDirectories(pattern)
                .Count(d => d.LinkTarget is null && named(d.Name) && !IsThe(d, except));
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }

    private static bool IsThe(DirectoryInfo directory, string? path) =>
        path is { Length: > 0 }
        && string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
