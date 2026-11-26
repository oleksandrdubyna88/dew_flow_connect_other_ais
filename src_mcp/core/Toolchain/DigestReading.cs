using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>What every reader of this family shares: lines, a safe match, a capped list. Pure; a line over <see cref="FailureDigest.LongestLine"/> is skipped and every pattern has a timeout, so no output costs more than its size.</summary>
internal static class DigestReading
{
    /// <summary>The lines of a text, each without a trailing carriage return and white space; a line over the limit is an empty line.</summary>
    internal static List<string> Lines(string text) =>
        [.. text.Split('\n').Select(line => line.Length > FailureDigest.LongestLine ? string.Empty : line.TrimEnd())];

    /// <summary>The match of a line, or null — also when the pattern timed out.</summary>
    internal static Match? Try(Regex regex, string line)
    {
        try
        {
            return regex.Match(line) is { Success: true } match ? match : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Whether the text holds a line the pattern matches.</summary>
    internal static bool Any(Regex regex, IEnumerable<string> lines) => lines.Any(line => Try(regex, line) is not null);

    /// <summary>A count of digits as a number; 0 when it does not fit.</summary>
    internal static int Count(string digits) => DigestText.Number(digits);

    /// <summary>The item: every text field redacted and cut as the digest has always done.</summary>
    internal static DigestItem Item(string step, string file, int line, string code, IEnumerable<string> message) =>
        new(step, DigestText.Head(file, FailureDigest.FieldChars), line, DigestText.Head(code, FailureDigest.FieldChars), DigestText.Head(string.Join('\n', message), FailureDigest.MessageChars));
}
