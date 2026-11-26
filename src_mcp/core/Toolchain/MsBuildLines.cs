using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The error lines of MSBuild output: <c>file(line,col): error CS0103: message [project]</c>, and the forms with no file — <c>MSBUILD : error MSB1009: …</c>, <c>proj.csproj : error NU1101: …</c>. MSBuild writes
/// each error twice, once as it happens and once in the summary under <c>Build FAILED.</c>, so a failure already read is not read again. Warnings are not errors here; a repository that turns them into errors
/// (<c>TreatWarningsAsErrors</c>) gets them as the errors they became.
/// </summary>
internal static partial class MsBuildLines
{
    internal static List<DigestItem> Read(string step, string text)
    {
        var seen = new HashSet<(string File, int Line, string Code, string Message)>();
        var items = new List<DigestItem>();
        foreach (var line in text.Split('\n').Where(line => line.Length <= FailureDigest.LongestLine))
        {
            if (Item(step, line.TrimEnd()) is { } item && seen.Add((item.File, item.Line, item.Code, item.Message)))
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static DigestItem? Item(string step, string line)
    {
        try
        {
            if (WithFile().Match(line) is { Success: true } located)
            {
                return Make(step, located.Groups["file"].Value, DigestText.Number(located.Groups["line"].Value), located);
            }

            return Bare().Match(line) is { Success: true } bare ? Make(step, FileOf(bare.Groups["tool"].Value), 0, bare) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            // A line that took a second to read is not an error line of MSBuild.
            return null;
        }
    }

    private static DigestItem Make(string step, string file, int line, Match match) =>
        new(step, DigestText.Head(file, FailureDigest.FieldChars), line, match.Groups["code"].Value, DigestText.Head(match.Groups["message"].Value.Trim(), FailureDigest.MessageChars));

    /// <summary>The tool named before <c> : error</c> is a file when it is a path or has an extension (<c>App.csproj</c>); <c>MSBUILD</c> and <c>CSC</c> are not.</summary>
    private static string FileOf(string tool) => tool.Contains('/') || tool.Contains('\\') || Path.HasExtension(tool) ? tool : string.Empty;

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+)(?:,\d+)*\): error (?<code>(?!TS\d)[A-Za-z]{1,10}\d+): (?<message>.*?)(?: \[[^\]]+\])?\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WithFile();

    [GeneratedRegex(@"^(?:(?<tool>\S.*?) : )?error (?<code>(?!TS\d)[A-Za-z]{1,10}\d+): (?<message>.*?)(?: \[[^\]]+\])?\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Bare();
}
