using System.Text.Json;

namespace CoaiMcp.Tests;

/// <summary>
/// What a test run may take from the machine's temp directory — <c>shared/temp-sweep.json</c>.
/// </summary>
/// <remarks>
/// Read, never restated: the extension's runner reads the same file (<c>scripts/sweepTemp.mjs</c>),
/// and a number copied into each half is the drift the file exists to prevent. The path climbs from
/// this binary's folder to the checkout, as <c>TheCommentRuleTests</c> reads its limit.
/// </remarks>
internal sealed record SweepRule(string Prefix, TimeSpan Keeps, IReadOnlyList<string> NeverSwept)
{
    public static SweepRule Shared()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "temp-sweep.json")));
        var root = json.RootElement;

        return new SweepRule(
            root.GetProperty("prefix").GetString() ?? string.Empty,
            TimeSpan.FromMinutes(root.GetProperty("keepMinutes").GetInt32()),
            [.. root.GetProperty("neverSwept").EnumerateArray().Select(e => e.GetString() ?? string.Empty)]);
    }
}
