using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The failed tests of a test executable's output — the shape the xUnit v3 / Microsoft Testing Platform runner of this repository prints (recorded in <c>fixtures/toolchain/mtp-three-failed.txt</c>):
/// <code>
/// failed Namespace.Class.Method(arg: 2) (55ms)
///   the assertion's message, one or more lines
///     at Frame.of.the.Stack() in /path/File.cs:24
/// </code>
/// One item per <c>failed</c> header: the test's full name as the <c>code</c>, the message of the lines between the header and the stack, and the first stack frame that has a file as the location. A passing or skipped
/// test, the run's own summary and the banner above it are not failures.
/// </summary>
internal static partial class TestExecutableFailures
{
    internal static List<DigestItem> Read(string step, string text)
    {
        var lines = text.Split('\n');
        var items = new List<DigestItem>();
        for (var at = 0; at < lines.Length; at++)
        {
            if (Header(lines[at]) is { Length: > 0 } name)
            {
                items.Add(Block(step, name, lines.Skip(at + 1).TakeWhile(IsInsideABlock)));
            }
        }

        return items;
    }

    /// <summary>The test's name when the line is a <c>failed …</c> header, or empty.</summary>
    private static string Header(string line)
    {
        try
        {
            return line.Length <= FailureDigest.LongestLine && FailedHeader().Match(line) is { Success: true } header ? header.Groups["name"].Value : string.Empty;
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }

    /// <summary>The lines under a header are indented; the next header, the summary and a blank line are not.</summary>
    private static bool IsInsideABlock(string line) => line.StartsWith("  ", StringComparison.Ordinal);

    private static DigestItem Block(string step, string name, IEnumerable<string> block)
    {
        var lines = block.Select(line => line.TrimEnd()).ToList();
        var message = lines.TakeWhile(line => !IsStackFrame(line)).Select(line => line.Trim());
        var (file, number) = Location(lines.Where(IsStackFrame));

        return new DigestItem(step, file, number, DigestText.Head(name, FailureDigest.FieldChars), DigestText.Head(string.Join('\n', message), FailureDigest.MessageChars));
    }

    private static bool IsStackFrame(string line) => line.TrimStart().StartsWith("at ", StringComparison.Ordinal);

    /// <summary>The first frame that names a file — the framework's frames have no symbols and no file.</summary>
    private static (string File, int Line) Location(IEnumerable<string> frames)
    {
        foreach (var frame in frames.Where(frame => frame.Length <= FailureDigest.LongestLine))
        {
            if (InFile().Match(frame) is { Success: true } located)
            {
                return (DigestText.Head(located.Groups["file"].Value, FailureDigest.FieldChars), DigestText.Number(located.Groups["line"].Value));
            }
        }

        return (string.Empty, 0);
    }

    [GeneratedRegex(@"^failed (?<name>.+) \(\d[^()]*\)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FailedHeader();

    [GeneratedRegex(@" in (?<file>.+):(?:line )?(?<line>\d+)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InFile();
}
