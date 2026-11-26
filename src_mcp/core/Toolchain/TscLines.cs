using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The error lines of <c>tsc</c> (E4 s4 t2; recorded in <c>fixtures/toolchain/node-tsc-two-errors.txt</c>): <c>src/index.ts(1,7): error TS2322: message</c>, and the forms with no file (<c>error TS18003: …</c>). The code must be a <c>TS</c> code: that is the marker of this tool, so an MSBuild line
/// (<c>CS0103</c>) is not read here and a tsc line is not read by <see cref="MsBuildLines"/>.
/// </summary>
internal static partial class TscLines
{
    internal static List<DigestItem> Read(string step, string text)
    {
        var seen = new HashSet<(string File, int Line, string Code, string Message)>();
        var items = new List<DigestItem>();
        foreach (var line in DigestReading.Lines(text))
        {
            DigestItem? item = null;
            if (DigestReading.Try(WithFile(), line) is { } located)
            {
                item = DigestReading.Item(step, located.Groups["file"].Value, DigestReading.Count(located.Groups["line"].Value), located.Groups["code"].Value, [located.Groups["message"].Value.Trim()]);
            }
            else if (DigestReading.Try(Bare(), line) is { } bare)
            {
                item = DigestReading.Item(step, string.Empty, 0, bare.Groups["code"].Value, [bare.Groups["message"].Value.Trim()]);
            }

            if (item is not null && seen.Add((item.File, item.Line, item.Code, item.Message)))
            {
                items.Add(item);
            }
        }

        return items;
    }

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+)(?:,\d+)*\): error (?<code>TS\d+): (?<message>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WithFile();

    [GeneratedRegex(@"^error (?<code>TS\d+): (?<message>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Bare();
}
