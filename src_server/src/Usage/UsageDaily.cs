using System.Globalization;

namespace CoaiServer;

/// <summary>One vendor's launches on one day. Zero is a measurement here: the ledger was read and had none.</summary>
public sealed record DayVendorDto(string Vendor, int Runs);

/// <summary>One UTC calendar day of the chart.</summary>
/// <param name="Day">
/// <c>yyyy-MM-dd</c>, a STRING the client prints as it is — never through <c>new Date()</c>, which
/// would bind it to the viewer's zone and shift a late-evening launch onto the next bar. The day is a
/// day, not an instant (<c>common/utc-timestamps.md</c>, rule 3).
/// </param>
/// <param name="Vendors">
/// A row for EVERY vendor seen anywhere in the chart's range, in a fixed order, so a stacked bar needs
/// no lookup and a vendor absent on a day is a zero rather than a hole.
/// </param>
public sealed record DayDto(string Day, IReadOnlyList<DayVendorDto> Vendors)
{
    /// <summary>Never null, whatever a deserialiser did with the property — see <see cref="UsageDto.Kinds"/>.</summary>
    public IReadOnlyList<DayVendorDto> Vendors { get; init; } = Vendors ?? [];
}

/// <summary>The launches-per-day chart under the company answer — thirty days, dense.</summary>
/// <param name="FromUtc">The first day's midnight.</param>
/// <param name="ToUtc">Tomorrow's midnight, exclusive: the range is half-open like every other here.</param>
public sealed record DailyDto(DateTimeOffset FromUtc, DateTimeOffset ToUtc, IReadOnlyList<DayDto> Days)
{
    /// <summary>Never null, whatever a deserialiser did with the property — see <see cref="UsageDto.Kinds"/>.</summary>
    public IReadOnlyList<DayDto> Days { get; init; } = Days ?? [];
}

/// <summary>
/// The chart's own range and its buckets — pure functions over parsed lines, like <see cref="UsageTotals"/>.
/// </summary>
/// <remarks>
/// <para>Story 2.2 of <c>PLAN_team_usage_by_person.md</c> (D7). The chart has ITS OWN range, whatever
/// window the summary was asked for: a page on <i>Today</i> still wants to see the month behind it,
/// and the reader used to keep only the selected window, so a chart built from that read would have
/// shown one bar. The endpoint reads the union of the two ranges once and hands this the whole scan,
/// which is why <see cref="Over"/> applies the chart's range itself rather than trusting its input.</para>
/// <para><b>Calendar days, not the summary's trailing thirty.</b> The <c>month</c> window is the
/// trailing 30 × 24 hours; this is the last thirty UTC calendar days including today, because a bar
/// is a day and a day has a date. The two are not the same box and the page says so.</para>
/// <para><b>Dense.</b> Thirty buckets whatever happened, with a vendor row of zero where nothing ran:
/// a day with no launches is a bar of zero height, which is a fact about the day, and a client that had
/// to invent the missing days would get the gaps wrong in exactly the way nobody checks.</para>
/// <para>Launches, not rounds: the ledger has no round id, so a bucket of lines is a bucket of
/// launches. The round-level counts are the every-round plan's.</para>
/// </remarks>
public static class UsageDaily
{
    /// <summary>How many calendar days the chart covers, today included.</summary>
    public const int Days = 30;

    /// <summary>The last <see cref="Days"/> UTC calendar days ending with today: <c>[today − 29 days 00:00, tomorrow 00:00)</c>.</summary>
    public static UsageRange Range(DateTimeOffset nowUtc)
    {
        var tomorrow = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);

        return new UsageRange(tomorrow.AddDays(-Days), tomorrow);
    }

    /// <summary>The chart over <paramref name="range"/>, from whatever lines it is handed.</summary>
    /// <remarks>
    /// ONE pass over the lines in range, counting by (day, vendor); the dense rows are then lookups
    /// into that count. The first version re-scanned each day's lines once per vendor — days × vendors
    /// passes over the same lines, for a number already known after one (code round on E1+E2,
    /// 2026-10-10). Byte-identical output, guarded by the daily tests.
    /// </remarks>
    public static DailyDto Over(IEnumerable<UsageLine> lines, UsageRange range)
    {
        var inRange = lines.Where(l => range.Contains(l.AtUtc)).ToList();
        var vendors = inRange
            .Select(l => l.Vendor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // The spelling each vendor is shown under: the one met first, whatever later lines spelled.
        var spelling = vendors.ToDictionary(vendor => vendor, vendor => vendor, StringComparer.OrdinalIgnoreCase);
        var launches = inRange
            .GroupBy(l => (Day: DayOf(l.AtUtc), Vendor: spelling[l.Vendor]))
            .ToDictionary(group => group.Key, group => group.Count());
        var dayCount = (int)Math.Ceiling((range.ToUtc - range.FromUtc).TotalDays);

        return new DailyDto(
            range.FromUtc,
            range.ToUtc,
            [.. Enumerable.Range(0, dayCount)
                .Select(offset => DayOf(range.FromUtc.AddDays(offset)))
                .Select(day => new DayDto(day, Bars(vendors, day, launches)))]);
    }

    /// <summary>The UTC calendar day an instant falls on, as the client will print it.</summary>
    private static string DayOf(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>One row per vendor the chart knows, in the chart's order — zero where the day has none.</summary>
    private static IReadOnlyList<DayVendorDto> Bars(
        IReadOnlyList<string> vendors, string day, IReadOnlyDictionary<(string Day, string Vendor), int> launches) =>
        [.. vendors.Select(vendor => new DayVendorDto(vendor, launches.GetValueOrDefault((day, vendor))))];
}
