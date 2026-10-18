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
/// <para><b>The formula E6's plan round fixed, corrected by story 6.2b.</b> Per window — identified by the limit's id AND its
/// duration, never by where the vendor filed it — and per account: an interval opens at a BASELINE reading and closes at the
/// first later reading of the SAME window instance (the same <c>resetsAt</c>) whose percentage ROSE above the baseline's; its
/// rounds are those counted since the baseline, its rise the closing percentage minus the baseline's. A plateau — readings
/// whose percentage did not rise — keeps the baseline and keeps its rounds: they accumulate until the rise. The closing
/// reading is the next baseline, so intervals never overlap and a round is counted in exactly one. A reading belongs to the
/// launch whose line carries it (the Team server reads the gauge right after a job and writes it on that job's lines), so
/// the rounds of a reading are the rounds of the lines carrying it.</para>
/// <para><b>A span is unusable — its ratio unavailable, never 0 or infinity</b> — when ANY reading in it (after the baseline,
/// up to and including the closing one) is missing — a launch before it has no reading of its own, it does not report the
/// window, or its percentage is outside [0, 100] — stale (older than its line by more than <see cref="StaleAfter"/>), from
/// another window instance (the window reset), or carries a review launch that sent no round id; the first such reading
/// in time order names the reason, and the next good reading starts a new baseline. A round that ran several launches on
/// the account counts in each reading by its share of those launches, so a code round's four reviewers are one round, not
/// four.</para>
/// <para><b>The figure</b> is POOLED: Σ rounds ÷ Σ rise over the last <see cref="LastIntervals"/> usable intervals across the
/// vendor's accounts — what one percent actually bought, not a mean of per-interval ratios; with none, it is unavailable
/// with the latest span's reason (<see cref="GaugeReasons.PercentDidNotRise"/> when a plateau is only waiting for a
/// rise). <b>Then the consumer rule:</b> a
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

    /// <summary>One interval: when it ended, its rounds and its rise — or why the span is unusable (or still waiting for a rise).</summary>
    private sealed record Interval(DateTimeOffset EndUtc, double Rounds, double Rise, string Unusable);

    /// <summary>
    /// An interval being walked: its baseline reading, the rounds of the readings after it, how many there are, and the
    /// latest one's instant. A baseline with no use (<see cref="Waiting"/>) means the walk is looking for one.
    /// </summary>
    private sealed record Span(Point Baseline, double Rounds, int After, DateTimeOffset LastUtc)
    {
        public bool HasBaseline => Baseline.Used is not null;
    }

    /// <summary>What walking one reading did: the span to carry on with, and the interval it closed or voided, if any.</summary>
    private sealed record Step(Span Next, IReadOnlyList<Interval> Emitted);

    /// <summary>The walk before it has a baseline: no reading yet that can open an interval.</summary>
    private static readonly Span Waiting = new(new Point(default, null, string.Empty, false), 0, 0, default);

    /// <summary>
    /// One account's readings, oldest first, and — for each reading after the first — whether a launch with no reading of
    /// its own lies between it and the one before. Built ONCE per account and shared by every window (epic 6's code
    /// round: the missing-reading check used to rescan every line for every interval of every window).
    /// </summary>
    private sealed record Account(IReadOnlyList<Reading> Readings, IReadOnlyList<bool> BareBefore);

    private static IEnumerable<GaugeRatio> FiguresOf(string vendor, IReadOnlyList<GaugeLine> lines, Func<string, IReadOnlyList<string>> uncounted)
    {
        var launches = LaunchesPerRound(lines);
        var accounts = AccountsOf(lines);

        return lines
            .SelectMany(line => Reported(line.Gauge).Select(one => one.Window))
            .Distinct()
            .OrderBy(window => window.LimitId, StringComparer.Ordinal)
            .ThenBy(window => window.Mins)
            .Select(window => Hidden(Figure(vendor, window, IntervalsOf(window, accounts, launches)), uncounted(window.LimitId)))
            .ToList();
    }

    /// <summary>Every account of the vendor, each read once.</summary>
    private static List<Account> AccountsOf(IEnumerable<GaugeLine> lines) =>
        [.. lines
            .GroupBy(line => line.Account, StringComparer.Ordinal)
            .Select(account => AccountOf([.. account.OrderBy(line => line.AtUtc)]))];

    /// <summary>
    /// One account, in ONE ordered pass: its readings, and for each interval whether a reading-less launch falls in
    /// <c>(before, after]</c> — a pointer over the reading-less lines' instants that only moves forward.
    /// </summary>
    private static Account AccountOf(IReadOnlyList<GaugeLine> ordered)
    {
        var readings = ReadingsOf(ordered);
        var bare = ordered.Where(line => ObservedOf(line.Gauge) is null).Select(line => line.AtUtc).ToList();
        var flags = new bool[readings.Count];
        var next = 0;
        for (var k = 1; k < readings.Count; k++)
        {
            next = FirstAfter(bare, next, readings[k - 1].ObservedUtc);
            flags[k] = next < bare.Count && bare[next] <= readings[k].ObservedUtc;
        }

        return new Account(readings, flags);
    }

    /// <summary>The first index from <paramref name="from"/> whose instant is after <paramref name="instant"/>.</summary>
    private static int FirstAfter(IReadOnlyList<DateTimeOffset> instants, int from, DateTimeOffset instant)
    {
        var at = from;
        while (at < instants.Count && instants[at] <= instant)
        {
            at++;
        }

        return at;
    }

    /// <summary>Every review launch of each round on this vendor — what a round's share of one interval is measured against.</summary>
    private static Dictionary<string, int> LaunchesPerRound(IEnumerable<GaugeLine> lines) =>
        lines
            .Where(line => line.IsReview && line.Round.Length > 0)
            .GroupBy(line => line.Round, StringComparer.Ordinal)
            .ToDictionary(round => round.Key, round => round.Count(), StringComparer.Ordinal);

    /// <summary>
    /// The windows a reading reports — every limit's, each with its use and its reset. A window with no limit or no
    /// stated length cannot be identified and is not one.
    /// </summary>
    private static IEnumerable<(Window Window, double? Used, string Resets)> Reported(GaugeOnLine gauge) =>
        ObservedOf(gauge) is null
            ? []
            : gauge.Windows
                .Where(window => window.LimitId.Length > 0 && window.Mins is not null)
                .Select(window => (new Window(window.LimitId, window.Mins!.Value), window.UsedPercent, window.ResetsUtc ?? string.Empty));

    private static DateTimeOffset? ObservedOf(GaugeOnLine gauge) =>
        DateTimeOffset.TryParse(
            gauge.ObservedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var at)
            ? at
            : null;

    /// <summary>Every interval of the window on every account, oldest end first.</summary>
    private static List<Interval> IntervalsOf(Window window, IReadOnlyList<Account> accounts, IReadOnlyDictionary<string, int> launches) =>
        [.. accounts
            .SelectMany(account => AccountIntervals(window, account, launches))
            .OrderBy(interval => interval.EndUtc)];

    /// <summary>
    /// One account's intervals, oldest first: the readings walked in order, a baseline kept through every reading that did
    /// not rise, the span closed at the rise or voided at its first barrier. A plateau still waiting for a rise at the end
    /// is no interval, and says so (<see cref="GaugeReasons.PercentDidNotRise"/>).
    /// </summary>
    private static List<Interval> AccountIntervals(Window window, Account account, IReadOnlyDictionary<string, int> launches)
    {
        var done = new List<Interval>();
        var span = Waiting;
        for (var k = 0; k < account.Readings.Count; k++)
        {
            var step = Advance(span, PointOf(window, account.Readings[k]), account.Readings[k], account.BareBefore[k], launches);
            span = step.Next;
            done.AddRange(step.Emitted);
        }

        done.AddRange(Trailing(span));

        return done;
    }

    /// <summary>A plateau that ended the walk without a rise is not an interval — but "the percentage did not rise" is why.</summary>
    private static IReadOnlyList<Interval> Trailing(Span span) =>
        span.HasBaseline && span.After > 0 ? [new Interval(span.LastUtc, 0, 0, GaugeReasons.PercentDidNotRise)] : [];

    /// <summary>One reading walked: it opens an interval when there is none, otherwise it extends, closes or voids it.</summary>
    private static Step Advance(Span span, Point point, Reading reading, bool bare, IReadOnlyDictionary<string, int> launches) =>
        span.HasBaseline ? Extend(span, point, reading, bare, launches) : new Step(Begin(point), []);

    /// <summary>
    /// A reading after the baseline: a barrier voids the span (and, when the reading itself is good, opens the next one); a rise
    /// closes it (the closing reading opens the next one); anything else is a plateau, which keeps its rounds.
    /// </summary>
    private static Step Extend(Span span, Point point, Reading reading, bool bare, IReadOnlyDictionary<string, int> launches)
    {
        var barrier = BarrierOf(span.Baseline, point, reading, bare);
        if (barrier.Length > 0)
        {
            return new Step(Begin(point), [new Interval(point.ObservedUtc, 0, 0, barrier)]);
        }

        var rounds = span.Rounds + RoundsIn(reading, launches);

        return point.Used > span.Baseline.Used
            ? new Step(Begin(point), [new Interval(point.ObservedUtc, rounds, point.Used!.Value - span.Baseline.Used!.Value, string.Empty)])
            : new Step(span with { Rounds = rounds, After = span.After + 1, LastUtc = point.ObservedUtc }, []);
    }

    /// <summary>A good reading opens an interval; one that cannot (missing, stale, no reset) leaves the walk looking for one.</summary>
    private static Span Begin(Point point) =>
        point.Used is not null && !point.Stale && point.ResetsUtc.Length > 0 ? new Span(point, 0, 0, point.ObservedUtc) : Waiting;

    /// <summary>The account's readings, by observation time — every line carrying one instant is one reading.</summary>
    private static List<Reading> ReadingsOf(IEnumerable<GaugeLine> lines) =>
        [.. lines
            .Select(line => (Line: line, At: ObservedOf(line.Gauge)))
            .Where(one => one.At is not null)
            .GroupBy(one => one.At!.Value)
            .OrderBy(reading => reading.Key)
            .Select(reading => new Reading(reading.Key, [.. reading.Select(one => one.Line)]))];

    /// <summary>What a reading says about the window, and whether it is stale against the lines that carry it.</summary>
    private static Point PointOf(Window window, Reading reading)
    {
        var said = reading.Lines.SelectMany(line => Reported(line.Gauge)).FirstOrDefault(one => one.Window == window);
        var stale = reading.Lines.Min(line => line.AtUtc) - reading.ObservedUtc > StaleAfter;

        return new Point(reading.ObservedUtc, InRange(said.Used), said.Resets ?? string.Empty, stale);
    }

    /// <summary>A percentage is within [0, 100]; anything else (or NaN) is no reading of the window, whatever the vendor sent.</summary>
    private static double? InRange(double? used) => used is >= 0 and <= 100 ? used : null;

    /// <summary>Why a reading voids the span opened at <paramref name="baseline"/>, in the order a person would check — or empty when it does not.</summary>
    private static string BarrierOf(Point baseline, Point point, Reading reading, bool bare)
    {
        (bool Hit, string Reason)[] checks =
        [
            (bare || point.Used is null, GaugeReasons.MissingReading),
            (point.Stale, GaugeReasons.StaleReading),
            (point.ResetsUtc.Length == 0, GaugeReasons.MissingReading),
            (!baseline.ResetsUtc.Equals(point.ResetsUtc, StringComparison.Ordinal), GaugeReasons.WindowReset),
            (HasRunWithoutRound(reading), GaugeReasons.RunWithoutRound),
        ];

        return checks.Where(check => check.Hit).Select(check => check.Reason).FirstOrDefault(string.Empty);
    }

    /// <summary>Whether a review launch of the reading sent no round id — its round cannot be counted or left out.</summary>
    private static bool HasRunWithoutRound(Reading reading) => reading.Lines.Any(line => line.IsReview && line.Round.Length == 0);

    /// <summary>The rounds of a reading's review lines, each by its share of the round's launches.</summary>
    private static double RoundsIn(Reading reading, IReadOnlyDictionary<string, int> launches) =>
        reading.Lines
            .Where(line => line.IsReview)
            .GroupBy(line => line.Round, StringComparer.Ordinal)
            .Sum(round => (double)round.Count() / launches[round.Key]);

    /// <summary>The pooled figure — Σ rounds ÷ Σ rise over the last usable intervals — or why there is none.</summary>
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
            _ => none with { RoundsPerPercent = rounds / usable.Sum(interval => interval.Rise), Intervals = usable.Count, Unavailable = string.Empty },
        };
    }

    /// <summary>The figure hidden when a known consumer of the limit is not counted — the window still named.</summary>
    private static GaugeRatio Hidden(GaugeRatio ratio, IReadOnlyList<string> uncounted) =>
        uncounted.Count == 0
            ? ratio
            : ratio with { RoundsPerPercent = null, Intervals = 0, Unavailable = GaugeReasons.ConsumerNotCounted, MissingConsumer = uncounted[0] };
}
