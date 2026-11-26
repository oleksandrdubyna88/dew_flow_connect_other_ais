using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The console of vitest (E4 s4 t3; recorded in <c>fixtures/toolchain/node-vitest-three-tests.txt</c>): <c> FAIL  sum.test.js &gt; fails on the value</c>, the assertion's message, <c> ❯ sum.test.js:4:50</c> for the location, and <c>Tests  2 failed | 1 passed (3)</c>. A suite that failed to run
/// (<c> FAIL  empty.test.js [ empty.test.js ]</c>) is an item and makes the run summary not complete. The marker of this tool is <c> RUN  v…</c> or its own summary line.
/// </summary>
internal static partial class VitestFailures
{
    internal const string Tool = "vitest";

    internal static bool Marks(List<string> lines) => DigestReading.Any(Run(), lines) || DigestReading.Any(Summary(), lines);

    internal static List<DigestItem> Read(string step, string text)
    {
        var lines = DigestReading.Lines(text);
        var items = new List<DigestItem>();
        if (!Marks(lines))
        {
            return items;
        }

        for (var at = 0; at < lines.Count; at++)
        {
            if (DigestReading.Try(TestFail(), lines[at]) is { } fail)
            {
                items.Add(Block(step, fail.Groups["file"].Value, fail.Groups["name"].Value, lines.Skip(at + 1).TakeWhile(IsInside)));
            }
            else if (DigestReading.Try(SuiteFail(), lines[at]) is { } suite)
            {
                items.Add(Block(step, suite.Groups["file"].Value, "Test suite failed to run", lines.Skip(at + 1).TakeWhile(IsInside)));
            }
        }

        return items;
    }

    internal static RunSummary? Summarize(string text, bool logCut)
    {
        var lines = DigestReading.Lines(text);
        if (!Marks(lines))
        {
            return null;
        }

        int total = 0, failed = 0, passed = 0;
        var found = false;
        foreach (var line in lines)
        {
            if (DigestReading.Try(Summary(), line) is { } sum)
            {
                found = true;
                total = DigestReading.Count(sum.Groups["total"].Value);
                failed = DigestReading.Try(Number("failed"), sum.Groups["parts"].Value) is { } f ? DigestReading.Count(f.Groups["n"].Value) : 0;
                passed = DigestReading.Try(Number("passed"), sum.Groups["parts"].Value) is { } p ? DigestReading.Count(p.Groups["n"].Value) : 0;
            }
        }

        var failures = lines.Select(line => DigestReading.Try(TestFail(), line)).Where(match => match is not null).Select(match => $"{match!.Groups["file"].Value} > {match.Groups["name"].Value}").Distinct(StringComparer.Ordinal).ToList();
        var unnamed = DigestReading.Any(SuiteFail(), lines);
        var passedNames = lines.Select(line => DigestReading.Try(Passed(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();

        return RunSummary.Make(Tool, found, total, failed, passed, failures, passedNames, logCut, unnamed);
    }

    /// <summary>The lines of a failure block: up to the next header or the separator line vitest draws between failures.</summary>
    private static bool IsInside(string line) => !line.StartsWith('⎯') && DigestReading.Try(TestFail(), line) is null && DigestReading.Try(SuiteFail(), line) is null;

    private static DigestItem Block(string step, string file, string name, IEnumerable<string> block)
    {
        var lines = block.ToList();
        var message = lines.SkipWhile(line => line.Trim().Length == 0).TakeWhile(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('❯') && !line.StartsWith("- Expected", StringComparison.Ordinal)).Select(line => line.Trim());
        var (where, number) = (file, 0);
        foreach (var line in lines)
        {
            if (DigestReading.Try(Location(), line) is { } located)
            {
                (where, number) = (located.Groups["file"].Value, DigestReading.Count(located.Groups["line"].Value));

                break;
            }
        }

        return DigestReading.Item(step, where, number, name, message);
    }

    private static Regex Number(string word) => new($@"(?<n>\d+) {word}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"^\s*RUN\s+v\d", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Run();

    [GeneratedRegex(@"^\s*Tests\s+(?<parts>[^()]*?)\s*\((?<total>\d+)\)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Summary();

    [GeneratedRegex(@"^\s*FAIL\s+(?<file>\S+) > (?<name>\S.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestFail();

    [GeneratedRegex(@"^\s*FAIL\s+(?<file>\S+) \[ .* \]\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SuiteFail();

    [GeneratedRegex(@"^\s*✓ (?<name>\S.*?)(?: \d+ms)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Passed();

    [GeneratedRegex(@"^\s*❯ (?<file>[^\s:]+):(?<line>\d+):\d+\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Location();
}
