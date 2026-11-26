using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The console of <c>node --test</c> (E4 s4 t3; recorded in <c>fixtures/toolchain/node-node-test-three-tests.txt</c>): the TAP-like spec lines <c>✔ name (1ms)</c> and <c>✖ name (1ms)</c>, the counters <c>ℹ tests 4</c>, <c>ℹ pass 2</c>, <c>ℹ fail 2</c>, and under <c>✖ failing tests:</c> one block per failure:
/// <c>test at sum.test.js:4:1</c>, <c>✖ name</c>, the message, indented <c>at</c> frames. The marker of this tool is <c>ℹ tests N</c> or <c>✖ failing tests:</c>.
/// </summary>
internal static partial class NodeTestFailures
{
    internal const string Tool = "node-test";

    internal static bool Marks(List<string> lines) => DigestReading.Any(Tests(), lines) || lines.Contains("✖ failing tests:");

    internal static List<DigestItem> Read(string step, string text)
    {
        var lines = DigestReading.Lines(text);
        var items = new List<DigestItem>();
        var section = lines.IndexOf("✖ failing tests:");
        if (!Marks(lines) || section < 0)
        {
            return items;
        }

        var (file, number) = (string.Empty, 0);
        for (var at = section + 1; at < lines.Count; at++)
        {
            if (DigestReading.Try(TestAt(), lines[at]) is { } located)
            {
                (file, number) = (located.Groups["file"].Value, DigestReading.Count(located.Groups["line"].Value));
            }
            else if (DigestReading.Try(Failed(), lines[at]) is { } header)
            {
                var message = lines.Skip(at + 1).TakeWhile(line => (line.Length == 0 || line.StartsWith("  ", StringComparison.Ordinal)) && !line.TrimStart().StartsWith("at ", StringComparison.Ordinal)).Select(line => line.Trim()).Where(line => line.Length > 0);
                items.Add(DigestReading.Item(step, file, number, header.Groups["name"].Value, message));
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

        var section = lines.IndexOf("✖ failing tests:");
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (DigestReading.Try(Counter(), line) is { } counter)
            {
                counters[counter.Groups["name"].Value] = DigestReading.Count(counter.Groups["n"].Value);
            }
        }

        var failures = section < 0 ? [] : lines.Skip(section + 1).Select(line => DigestReading.Try(Failed(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();
        var head = section < 0 ? lines : lines.Take(section).ToList();
        var passedNames = head.Select(line => DigestReading.Try(Passed(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();
        var found = counters.ContainsKey("tests") && counters.ContainsKey("fail");

        return RunSummary.Make(Tool, found, counters.GetValueOrDefault("tests"), counters.GetValueOrDefault("fail"), counters.GetValueOrDefault("pass"), failures, passedNames, logCut, unnamed: false);
    }

    [GeneratedRegex(@"^ℹ tests \d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tests();

    [GeneratedRegex(@"^ℹ (?<name>tests|pass|fail) (?<n>\d+)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Counter();

    [GeneratedRegex(@"^test at (?<file>[^:\s]+):(?<line>\d+):\d+\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestAt();

    [GeneratedRegex(@"^✖ (?<name>\S.*?) \(\d[^()]*\)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Failed();

    [GeneratedRegex(@"^✔ (?<name>\S.*?) \(\d[^()]*\)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Passed();
}
