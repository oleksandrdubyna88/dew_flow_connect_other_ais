using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The console of <c>dotnet test</c> in its VSTest form (E4 s4 t1; recorded in <c>fixtures/toolchain/dotnet-vstest-failed.txt</c>): <c>  Failed Namespace.Class.Method [3 ms]</c>, an <c>Error Message:</c> block, a <c>Stack Trace:</c> block whose first
/// frame with a file gives the location, and the summary line <c>Failed!  - Failed: 2, Passed: 2, Skipped: 0, Total: 4</c>. The MTP form of <c>dotnet test</c> is read by <see cref="TestExecutableFailures"/>. A reader needs a marker line of its own tool first
/// (<c>VSTest version</c>, <c>Starting test execution</c> or the summary line), so another tool's output gives no item.
/// </summary>
internal static partial class DotnetTestFailures
{
    internal const string Tool = "vstest";

    internal static bool Marks(string text) => DigestReading.Any(Marker(), DigestReading.Lines(text));

    internal static List<DigestItem> Read(string step, string text)
    {
        var lines = DigestReading.Lines(text);
        var items = new List<DigestItem>();
        if (!DigestReading.Any(Marker(), lines))
        {
            return items;
        }

        for (var at = 0; at < lines.Count; at++)
        {
            if (DigestReading.Try(Failed(), lines[at]) is { } header)
            {
                items.Add(Block(step, header.Groups["name"].Value, lines.Skip(at + 1).TakeWhile(line => DigestReading.Try(AnyHeader(), line) is null && DigestReading.Try(Summary(), line) is null)));
            }
        }

        return items;
    }

    internal static RunSummary? Summarize(string text, bool logCut)
    {
        var lines = DigestReading.Lines(text);
        if (!DigestReading.Any(Marker(), lines))
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
                failed += DigestReading.Count(sum.Groups["failed"].Value);
                passed += DigestReading.Count(sum.Groups["passed"].Value);
                total += DigestReading.Count(sum.Groups["total"].Value);
            }
        }

        if (!found && lines.Any(line => line.StartsWith("Test Run ", StringComparison.Ordinal)))
        {
            // The older form of the same console (the detailed run): `Test Run Failed.`, then `Total tests: 4`, `     Passed: 2`, `     Failed: 2`.
            foreach (var line in lines)
            {
                if (DigestReading.Try(OldCounter(), line) is { } counter)
                {
                    found = true;
                    var n = DigestReading.Count(counter.Groups["n"].Value);
                    (total, passed, failed) = counter.Groups["name"].Value switch { "Total tests" => (n, passed, failed), "Passed" => (total, n, failed), _ => (total, passed, n) };
                }
            }
        }

        var failures = lines.Select(line => DigestReading.Try(Failed(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();
        var passedNames = lines.Select(line => DigestReading.Try(Passed(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();

        return RunSummary.Make(Tool, found, total, failed, passed, failures, passedNames, logCut, unnamed: false);
    }

    private static DigestItem Block(string step, string name, IEnumerable<string> block)
    {
        var lines = block.ToList();
        var message = lines.SkipWhile(line => !line.TrimStart().StartsWith("Error Message:", StringComparison.Ordinal)).Skip(1).TakeWhile(line => !line.TrimStart().StartsWith("Stack Trace:", StringComparison.Ordinal)).Select(line => line.Trim()).Where(line => line.Length > 0);
        var (file, number) = (string.Empty, 0);
        foreach (var frame in lines.Where(line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal)))
        {
            if (DigestReading.Try(InFile(), frame) is { } located)
            {
                (file, number) = (located.Groups["file"].Value, DigestReading.Count(located.Groups["line"].Value));

                break;
            }
        }

        return DigestReading.Item(step, file, number, name, message);
    }

    [GeneratedRegex(@"^(?:VSTest version|Starting test execution|(?:Passed|Failed)!\s+- Failed:\s+\d+, Passed:)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Marker();

    [GeneratedRegex(@"^\s*(?<name>Total tests|Passed|Failed): (?<n>\d+)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OldCounter();

    [GeneratedRegex(@"^\s{2}Failed (?<name>\S.*?) \[[^\]]*\]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Failed();

    [GeneratedRegex(@"^\s{2}Passed (?<name>\S.*?) \[[^\]]*\]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Passed();

    [GeneratedRegex(@"^\s{2}(?:Failed|Passed|Skipped) \S", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AnyHeader();

    [GeneratedRegex(@"^(?:Passed|Failed)!\s+- Failed:\s+(?<failed>\d+), Passed:\s+(?<passed>\d+), Skipped:\s+\d+, Total:\s+(?<total>\d+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Summary();

    [GeneratedRegex(@" in (?<file>.+):line (?<line>\d+)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InFile();
}
