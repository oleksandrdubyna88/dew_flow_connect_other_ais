using System.Text.Json;

namespace CoaiMcp.Tests;

/// <summary>
/// What the product makes in the machine's temp directory and no test may take — <c>shared/temp-sweep.json</c>.
/// </summary>
/// <remarks>
/// Read, never restated: the extension's runner reads the same file (<c>scripts/startOfRun.mjs</c>), and a list copied into each half is the drift the file
/// exists to prevent. Since E5 S2-A nothing selects by age or prefix here: the file holds only the product's own prefixes, which the start-of-run count leaves
/// out and the one-off clean-up refuses. The path climbs from this binary's folder to the checkout, as <c>TheCommentRuleTests</c> reads its limit.
/// </remarks>
internal sealed record SweepRule(IReadOnlyList<string> NeverSwept)
{
    public static SweepRule Shared()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "temp-sweep.json")));

        return new SweepRule([.. json.RootElement.GetProperty("neverSwept").EnumerateArray().Select(e => e.GetString() ?? string.Empty)]);
    }
}
