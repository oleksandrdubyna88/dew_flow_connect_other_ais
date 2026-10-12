using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// Rounds per 1 % of each gauge window, as <c>/api/usage</c> answers it (story 6.2 of todo/PLAN_every_round_is_counted.md):
/// the server's ledger lines handed to the ONE derivation both halves use, <see cref="RoundsPerPercent"/>.
/// </summary>
/// <remarks>
/// <para><b>Over every caller's lines, whatever the scope.</b> A gauge is the ACCOUNT's, and every launch on the account spent
/// it — counting only one person's rounds against the account's whole rise would be a figure about nobody. The answer
/// names a vendor, a window and a ratio; never a person, an account or an email.</para>
/// <para><b>A week and a day back</b>, whatever windows were asked: a weekly window is the longest any vendor reports, so an
/// interval older than that belongs to an instance of the window that has already reset.</para>
/// <para>A budget refusal launched nothing (story 4.3) and is left out; every other line is the derivation's to judge — a
/// launch with no reading after it makes its interval unusable rather than being skipped.</para>
/// </remarks>
internal static class UsageGauges
{
    /// <summary>How far back the derivation reads.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(8);

    private static readonly string NoBudgetOutcome = nameof(FailureKind.NoBudget);

    /// <summary>The range the derivation reads, ending now.</summary>
    public static UsageRange Range(DateTimeOffset nowUtc) => new(nowUtc - Lookback, nowUtc.AddTicks(1));

    /// <summary>Every gauge window's figure — or null, so the key is absent, when the request asked no round windows.</summary>
    public static IReadOnlyList<GaugeRatio>? Of(UsageScan scan, IReadOnlyList<NamedRange> windows, DateTimeOffset nowUtc)
    {
        if (windows.Count == 0)
        {
            return null;
        }
        var range = Range(nowUtc);

        return RoundsPerPercent.Derive(scan.Lines
            .Where(line => range.Contains(line.AtUtc) && !line.Outcome.Equals(NoBudgetOutcome, StringComparison.Ordinal))
            .Select(AsGaugeLine)).Ratios;
    }

    private static GaugeLine AsGaugeLine(UsageLine line) =>
        new(line.AtUtc, line.Vendor, line.Slot, line.Round, line.Kind == JobKind.Review, line.Gauge);
}
