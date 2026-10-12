namespace CoaiMcp.Runners.Reviewers;

/// <summary>One ledger line as the rounds-per-1 % derivation reads it (story 6.2 of todo/PLAN_every_round_is_counted.md).</summary>
/// <param name="AtUtc">When the line was written.</param>
/// <param name="Vendor">The vendor row the launch belonged to.</param>
/// <param name="Account">The account (the Team server's slot) the launch ran on — readings of two accounts are never paired.</param>
/// <param name="Round">The client's round id, or empty when the request sent none.</param>
/// <param name="IsReview">Whether the line is a review launch — the only kind a round is counted from.</param>
/// <param name="Gauge">The reading taken on the account right after the launch, or <see cref="GaugeOnLine.None"/>.</param>
public sealed record GaugeLine(DateTimeOffset AtUtc, string Vendor, string Account, string Round, bool IsReview, GaugeOnLine Gauge);

/// <summary>One window's rounds per 1 %, or why there is none.</summary>
/// <param name="Vendor">The vendor row.</param>
/// <param name="LimitId">The vendor's limit identity (<c>codex</c>).</param>
/// <param name="WindowMins">The window's length — with <paramref name="LimitId"/>, its identity.</param>
/// <param name="RoundsPerPercent">The figure — null, never 0 or infinity, unless it is available AND every consumer is counted.</param>
/// <param name="Intervals">How many usable intervals the figure was taken over (0 when unavailable).</param>
/// <param name="Unavailable">A <see cref="GaugeReasons"/> code, or empty when the figure is shown.</param>
/// <param name="MissingConsumer">A <see cref="GaugeConsumers"/> code when the figure is hidden for an uncounted consumer, else empty.</param>
public sealed record GaugeRatio(
    string Vendor,
    string LimitId,
    long WindowMins,
    double? RoundsPerPercent,
    int Intervals,
    string Unavailable,
    string MissingConsumer);

/// <summary>Every window's figure, or — where nothing was read — why not.</summary>
/// <param name="Ratios">One per vendor and window the readings named; empty when there are none.</param>
/// <param name="Unavailable">Empty when the readings were looked at; a <see cref="GaugeReasons"/> code when there were none to look at.</param>
public sealed record GaugeAnswer(IReadOnlyList<GaugeRatio> Ratios = null!, string Unavailable = "")
{
    /// <summary>Never null, whatever a deserialiser or a caller did with the property — no ratios is an empty list.</summary>
    public IReadOnlyList<GaugeRatio> Ratios { get; init; } = Ratios ?? [];
}

/// <summary>Why a figure is not a number — fixed codes, translated into words by the client.</summary>
public static class GaugeReasons
{
    /// <summary>Fewer than two readings of the window on any one account: there is no interval at all.</summary>
    public const string TooFewReadings = "too-few-readings";

    /// <summary>The percentage stayed the same or fell between the two readings — a ratio would be infinite or negative.</summary>
    public const string PercentDidNotRise = "percent-did-not-rise";

    /// <summary>The window reset between the two readings (its <c>resetsAt</c> changed): two instances of the window.</summary>
    public const string WindowReset = "window-reset";

    /// <summary>A reading was older than the line that carried it by more than the slack: not the account's state after that launch.</summary>
    public const string StaleReading = "stale-reading";

    /// <summary>A launch between the readings has no reading of its own, or a reading does not report the window.</summary>
    public const string MissingReading = "missing-reading";

    /// <summary>A review launch between the readings sent no round id, so the rounds between them cannot be counted.</summary>
    public const string RunWithoutRound = "run-without-round";

    /// <summary>Usable intervals exist, but no round was counted in them — a rounds-weighted mean of nothing.</summary>
    public const string NoRounds = "no-rounds";

    /// <summary>A consumer of the account is known and not counted: the figure is hidden (<see cref="GaugeRatio.MissingConsumer"/> says which).</summary>
    public const string ConsumerNotCounted = "consumer-not-counted";

    /// <summary>This machine does not read the gauge — local capture (story 6.1b) is deferred.</summary>
    public const string LocalCaptureDeferred = "local-capture-deferred";
}

/// <summary>The consumers of an account that coai knows of and cannot count, per limit (RESULTS_vendor_quota_gauges.md §2.4, §6.3).</summary>
public static class GaugeConsumers
{
    /// <summary>The account's owner using codex directly, outside coai — it spends the same limit and writes no ledger line.</summary>
    public const string DirectCodexUse = "direct-codex-use";

    /// <summary>The known, uncounted consumers of a limit — empty when every known consumer is counted.</summary>
    public static IReadOnlyList<string> UncountedFor(string limitId) =>
        limitId.Equals("codex", StringComparison.OrdinalIgnoreCase) ? [DirectCodexUse] : [];
}

/// <summary>Rounds per 1 % of a vendor's window — computed ONCE, here, for both halves (story 6.2).</summary>
/// <remarks>
/// <para><b>The formula E6's plan round fixed.</b> Per window — identified by the limit's id AND its duration, never by where
/// the vendor filed it — and per account, between two consecutive readings of the SAME window instance (the same
/// <c>resetsAt</c>): the rounds counted between them over the rise in the percentage. A reading belongs to the launch whose
/// line carries it (the Team server reads the gauge right after a job and writes it on that job's lines), so the rounds
/// "between" two readings are the rounds of the lines carrying the later one.</para>
/// <para><b>An interval is unusable — its ratio unavailable, never 0 or infinity</b> — when a launch between the readings
/// has no reading of its own or a reading does not report the window (missing), when a reading is older than its line by
/// more than <see cref="StaleAfter"/> (stale), when the window reset between them, when the percentage did not rise, or
/// when a review launch between them sent no round id. A round that ran several launches on the account counts in each
/// interval by its share of those launches, so a code round's four reviewers are one round, not four.</para>
/// <para><b>The figure</b> is the rounds-weighted mean of the last <see cref="LastIntervals"/> usable intervals across the
/// vendor's accounts; with none, it is unavailable with the latest interval's reason. <b>Then the consumer rule:</b> a
/// limit with a known consumer coai cannot count (<see cref="GaugeConsumers"/>) has its figure hidden, with that consumer
/// named — the observed gauge and its reset are the catalog's and are shown regardless.</para>
/// <para>Pure: lines in, figures out. The Team server hands it its ledger (<c>/api/usage</c>); <c>coai-mcp
/// --round-counts</c> answers <see cref="NotReadHere"/>, because local capture is deferred.</para>
/// </remarks>
public static class RoundsPerPercent
{
    /// <summary>How many of the latest usable intervals the figure is the mean of.</summary>
    public const int LastIntervals = 10;

    /// <summary>How much older than its line a reading may be and still be the account's state after that launch.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>What a machine that does not read the gauge answers: no figure, and why.</summary>
    public static GaugeAnswer NotReadHere { get; } = new([], GaugeReasons.LocalCaptureDeferred);

    /// <summary>Every window's figure from the ledger's lines, with the known uncounted consumers hiding it.</summary>
    public static GaugeAnswer Derive(IEnumerable<GaugeLine> lines) => Derive(lines, GaugeConsumers.UncountedFor);

    /// <summary>Every window's figure, with <paramref name="uncounted"/> naming each limit's uncounted consumers.</summary>
    public static GaugeAnswer Derive(IEnumerable<GaugeLine> lines, Func<string, IReadOnlyList<string>> uncounted)
    {
        var ratios = lines
            .GroupBy(line => line.Vendor, StringComparer.Ordinal)
            .OrderBy(vendor => vendor.Key, StringComparer.Ordinal)
            .SelectMany(vendor => FiguresOf(vendor.Key, [.. vendor], uncounted))
            .ToList();

        return new GaugeAnswer(ratios, string.Empty);
    }

    /// <summary>One window as the formula identifies it: the limit and the length.</summary>
    private sealed record Window(string LimitId, long Mins);

    /// <summary>What one reading says about one window — its use (null when it did not report the window) and its reset.</summary>
    private sealed record Point(DateTimeOffset ObservedUtc, double? Used, string ResetsUtc, bool Stale);

    /// <summary>One reading and the lines that carry it.</summary>
    private sealed record Reading(DateTimeOffset ObservedUtc, IReadOnlyList<GaugeLine> Lines);

    /// <summary>One interval between two readings: when it ended, its rounds and its rise — or why it is unusable.</summary>
    private sealed record Interval(DateTimeOffset EndUtc, double Rounds, double Rise, string Unusable);

    private static IEnumerable<GaugeRatio> FiguresOf(string vendor, IReadOnlyList<GaugeLine> lines, Func<string, IReadOnlyList<string>> uncounted)
    {
        var launches = LaunchesPerRound(lines);

        return lines
            .SelectMany(line => Reported(line.Gauge).Select(one => one.Window))
            .Distinct()
            .OrderBy(window => window.LimitId, StringComparer.Ordinal)
            .ThenBy(window => window.Mins)
            .Select(window => Hidden(Figure(vendor, window, IntervalsOf(window, lines, launches)), uncounted(window.LimitId)));
    }

    /// <summary>Every review launch of each round on this vendor — what a round's share of one interval is measured against.</summary>
    private static Dictionary<string, int> LaunchesPerRound(IEnumerable<GaugeLine> lines) =>
        lines
            .Where(line => line.IsReview && line.Round.Length > 0)
            .GroupBy(line => line.Round, StringComparer.Ordinal)
            .ToDictionary(round => round.Key, round => round.Count(), StringComparer.Ordinal);

    /// <summary>The windows a reading reports: its primary and secondary, each with its use and its reset.</summary>
    private static IEnumerable<(Window Window, double? Used, string Resets)> Reported(GaugeOnLine gauge)
    {
        if (gauge.LimitId.Length == 0 || ObservedOf(gauge) is null)
        {
            yield break;
        }
        if (gauge.PrimaryMins is { } primary)
        {
            yield return (new Window(gauge.LimitId, primary), gauge.PrimaryUsedPercent, gauge.PrimaryResetsUtc);
        }
        if (gauge.SecondaryMins is { } secondary)
        {
            yield return (new Window(gauge.LimitId, secondary), gauge.SecondaryUsedPercent, gauge.SecondaryResetsUtc);
        }
    }

    private static DateTimeOffset? ObservedOf(GaugeOnLine gauge) =>
        DateTimeOffset.TryParse(
            gauge.ObservedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var at)
            ? at
            : null;

    /// <summary>Every interval of the window on every account, oldest end first.</summary>
    private static List<Interval> IntervalsOf(Window window, IReadOnlyList<GaugeLine> lines, IReadOnlyDictionary<string, int> launches) =>
        [.. lines
            .GroupBy(line => line.Account, StringComparer.Ordinal)
            .SelectMany(account => AccountIntervals(window, [.. account.OrderBy(line => line.AtUtc)], launches))
            .OrderBy(interval => interval.EndUtc)];

    /// <summary>One account's intervals: each pair of consecutive readings, with the lines between them.</summary>
    private static IEnumerable<Interval> AccountIntervals(Window window, IReadOnlyList<GaugeLine> lines, IReadOnlyDictionary<string, int> launches)
    {
        var readings = ReadingsOf(lines);

        return readings.Zip(readings.Skip(1), (before, after) =>
            Between(PointOf(window, before), PointOf(window, after), after, Bare(lines, before, after), launches));
    }

    /// <summary>The account's readings, by observation time — every line carrying one instant is one reading.</summary>
    private static List<Reading> ReadingsOf(IEnumerable<GaugeLine> lines) =>
        [.. lines
            .Select(line => (Line: line, At: ObservedOf(line.Gauge)))
            .Where(one => one.At is not null)
            .GroupBy(one => one.At!.Value)
            .OrderBy(reading => reading.Key)
            .Select(reading => new Reading(reading.Key, [.. reading.Select(one => one.Line)]))];

    /// <summary>The lines written between two readings that carry none — launches whose reading is missing.</summary>
    private static bool Bare(IEnumerable<GaugeLine> lines, Reading before, Reading after) =>
        lines.Any(line => ObservedOf(line.Gauge) is null && line.AtUtc > before.ObservedUtc && line.AtUtc <= after.ObservedUtc);

    /// <summary>What a reading says about the window, and whether it is stale against the lines that carry it.</summary>
    private static Point PointOf(Window window, Reading reading)
    {
        var said = reading.Lines.SelectMany(line => Reported(line.Gauge)).FirstOrDefault(one => one.Window == window);
        var stale = reading.Lines.Min(line => line.AtUtc) - reading.ObservedUtc > StaleAfter;

        return new Point(reading.ObservedUtc, said.Used, said.Resets ?? string.Empty, stale);
    }

    /// <summary>One interval — its rounds over its rise, or the first reason it cannot be one.</summary>
    private static Interval Between(Point before, Point after, Reading closing, bool bare, IReadOnlyDictionary<string, int> launches)
    {
        var unusable = Unusable(before, after, bare);
        if (unusable.Length > 0)
        {
            return new Interval(after.ObservedUtc, 0, 0, unusable);
        }

        return closing.Lines.Any(line => line.IsReview && line.Round.Length == 0)
            ? new Interval(after.ObservedUtc, 0, 0, GaugeReasons.RunWithoutRound)
            : new Interval(after.ObservedUtc, RoundsIn(closing, launches), after.Used!.Value - before.Used!.Value, string.Empty);
    }

    /// <summary>Why two readings do not make an interval, in the order a person would check — or empty when they do.</summary>
    private static string Unusable(Point before, Point after, bool bare) =>
        (before, after) switch
        {
            _ when bare || before.Used is null || after.Used is null => GaugeReasons.MissingReading,
            _ when before.Stale || after.Stale => GaugeReasons.StaleReading,
            _ when before.ResetsUtc.Length == 0 || after.ResetsUtc.Length == 0 => GaugeReasons.MissingReading,
            _ when !before.ResetsUtc.Equals(after.ResetsUtc, StringComparison.Ordinal) => GaugeReasons.WindowReset,
            _ when after.Used <= before.Used => GaugeReasons.PercentDidNotRise,
            _ => string.Empty,
        };

    /// <summary>The rounds of the closing reading's review lines, each by its share of the round's launches.</summary>
    private static double RoundsIn(Reading closing, IReadOnlyDictionary<string, int> launches) =>
        closing.Lines
            .Where(line => line.IsReview)
            .GroupBy(line => line.Round, StringComparer.Ordinal)
            .Sum(round => (double)round.Count() / launches[round.Key]);

    /// <summary>The rounds-weighted mean of the last usable intervals, or why there is none.</summary>
    private static GaugeRatio Figure(string vendor, Window window, IReadOnlyList<Interval> intervals)
    {
        var none = new GaugeRatio(vendor, window.LimitId, window.Mins, null, 0, GaugeReasons.TooFewReadings, string.Empty);
        var usable = intervals.Where(interval => interval.Unusable.Length == 0).TakeLast(LastIntervals).ToList();
        var rounds = usable.Sum(interval => interval.Rounds);

        return (intervals.Count, usable.Count, rounds) switch
        {
            (0, _, _) => none,
            (_, 0, _) => none with { Unavailable = intervals[^1].Unusable },
            (_, _, <= 0) => none with { Unavailable = GaugeReasons.NoRounds },
            _ => none with { RoundsPerPercent = usable.Sum(i => i.Rounds * i.Rounds / i.Rise) / rounds, Intervals = usable.Count, Unavailable = string.Empty },
        };
    }

    /// <summary>The figure hidden when a known consumer of the limit is not counted — the window still named.</summary>
    private static GaugeRatio Hidden(GaugeRatio ratio, IReadOnlyList<string> uncounted) =>
        uncounted.Count == 0
            ? ratio
            : ratio with { RoundsPerPercent = null, Intervals = 0, Unavailable = GaugeReasons.ConsumerNotCounted, MissingConsumer = uncounted[0] };
}
