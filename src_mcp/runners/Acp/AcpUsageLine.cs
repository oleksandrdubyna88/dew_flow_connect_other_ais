using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// Reads the token totals out of the agent's answer to <c>/usage</c> (D15, RESULTS §5): the line
/// <c>Session total: 19973 input, 31 output</c>, the numbers with or without thousands commas.
/// </summary>
public static class AcpUsageLine
{
    private static readonly Regex Total = new(
        @"Session total:\s*([\d,]+)\s+input,\s*([\d,]+)\s+output", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The totals the text carries — or <see cref="AcpTokens.Unknown"/> for anything else (<c>no LLM calls yet</c>, no line, empty, a number no <c>long</c> holds).</summary>
    public static AcpTokens Parse(string text)
    {
        try
        {
            return Read(Total.Match(text));
        }
        catch (RegexMatchTimeoutException)
        {
            // The agent's text is outside this process's control; a match that takes a second is not a number.
            return AcpTokens.Unknown;
        }
    }

    private static AcpTokens Read(Match line) =>
        line.Success && Number(line.Groups[1].Value) is var input and >= 0 && Number(line.Groups[2].Value) is var output and >= 0
            ? new AcpTokens(input, output, true)
            : AcpTokens.Unknown;

    /// <summary>The digits with their thousands commas dropped — culture-invariant — or -1 when they do not fit a <c>long</c>.</summary>
    private static long Number(string digits) =>
        long.TryParse(digits.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1;
}
