using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The console of jest (E4 s4 t3; recorded in <c>fixtures/toolchain/node-jest-three-tests.txt</c>): <c>FAIL ./sum.test.js</c>, then <c>  ● name</c> blocks with the assertion's message, a code frame and <c>at Object.toBe (sum.test.js:2:50)</c> frames, and the summary
/// <c>Tests:  2 failed, 1 passed, 3 total</c>. One item per failed test: the name, the file of its <c>FAIL</c> line, the first frame outside <c>node_modules</c> as the line, the message above the code frame. <c>● Test suite failed to run</c> is a failure the summary cannot name: it is an item and
/// it makes the run summary not complete. The marker of this tool is its own summary line or a <c>FAIL</c> line followed by a <c>●</c> block.
/// </summary>
internal static partial class JestFailures
{
    internal const string Tool = "jest";

    private const string SuiteFailed = "Test suite failed to run";

    internal static bool Marks(List<string> lines) => DigestReading.Any(Summary(), lines) || DigestReading.Any(SuiteSummary(), lines) || (DigestReading.Any(FailLine(), lines) && DigestReading.Any(Bullet(), lines));

    internal static List<DigestItem> Read(string step, string text)
    {
        var lines = DigestReading.Lines(text);
        var items = new List<DigestItem>();
        if (!Marks(lines))
        {
            return items;
        }

        var file = string.Empty;
        for (var at = 0; at < lines.Count; at++)
        {
            if (DigestReading.Try(FailLine(), lines[at]) is { } fail)
            {
                file = Clean(fail.Groups["file"].Value);
            }
            else if (DigestReading.Try(PassLine(), lines[at]) is not null)
            {
                file = string.Empty;
            }
            else if (DigestReading.Try(Bullet(), lines[at]) is { } bullet)
            {
                items.Add(Block(step, file, bullet.Groups["name"].Value, lines.Skip(at + 1).TakeWhile(line => DigestReading.Try(Bullet(), line) is null && DigestReading.Try(FailLine(), line) is null && DigestReading.Try(PassLine(), line) is null && DigestReading.Try(SuiteSummary(), line) is null)));
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
                failed = DigestReading.Count(sum.Groups["failed"].Value);
                passed = DigestReading.Count(sum.Groups["passed"].Value);
                total = DigestReading.Count(sum.Groups["total"].Value);
            }
        }

        var bullets = lines.Select(line => DigestReading.Try(Bullet(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();
        var failures = bullets.Where(name => name != SuiteFailed).ToList();
        var passedNames = lines.Select(line => DigestReading.Try(Passed(), line)).Where(match => match is not null).Select(match => match!.Groups["name"].Value).ToList();

        return RunSummary.Make(Tool, found, total, failed, passed, failures, passedNames, logCut, unnamed: bullets.Contains(SuiteFailed));
    }

    private static string Clean(string file) => file.StartsWith("./", StringComparison.Ordinal) ? file[2..] : file;

    private static DigestItem Block(string step, string file, string name, IEnumerable<string> block)
    {
        var lines = block.ToList();
        var message = lines.TakeWhile(line => DigestReading.Try(CodeFrame(), line) is null && DigestReading.Try(Frame(), line) is null).Select(line => line.Trim()).Where(line => line.Length > 0);
        var (where, number) = (file, 0);
        foreach (var line in lines)
        {
            if (DigestReading.Try(Frame(), line) is { } frame && !frame.Groups["file"].Value.Contains("node_modules", StringComparison.Ordinal))
            {
                (where, number) = (Clean(frame.Groups["file"].Value), DigestReading.Count(frame.Groups["line"].Value));

                break;
            }
        }

        return DigestReading.Item(step, where, number, name, message);
    }

    [GeneratedRegex(@"^\s*Tests:\s+(?:(?<failed>\d+) failed, )?(?:\d+ skipped, )?(?:\d+ todo, )?(?:(?<passed>\d+) passed, )?(?<total>\d+) total\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Summary();

    [GeneratedRegex(@"^\s*Test Suites:\s+\d", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SuiteSummary();

    [GeneratedRegex(@"^FAIL (?<file>\S+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FailLine();

    [GeneratedRegex(@"^PASS (?<file>\S+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PassLine();

    [GeneratedRegex(@"^  ● (?<name>\S.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s+✓ (?<name>\S.*?)(?: \(\d+ ?ms\))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Passed();

    [GeneratedRegex(@"^\s+(?:>\s*)?\d+ \||^\s+\|", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeFrame();

    [GeneratedRegex(@"^\s+at (?:.*\()?(?<file>[^():\s]+):(?<line>\d+):\d+\)?\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Frame();
}
