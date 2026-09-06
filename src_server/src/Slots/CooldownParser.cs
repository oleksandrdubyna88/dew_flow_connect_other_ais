using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiServer;

/// <summary>
/// How long to leave an account alone, read from the sentence the vendor itself printed.
/// </summary>
/// <remarks>
/// <para><b>Every pattern here matches a line somebody actually saw.</b> That is the same discipline
/// <c>ReviewerExecutor.Phrases</c> keeps for detecting a rate limit at all, and for the same reason:
/// a guessed pattern is a test that passes and a production path that never fires. The captured
/// lines are listed on each pattern; a new pattern arrives with the line that motivated it.</para>
/// <para><b>The fallback errs LONG, never short.</b> Waiting too long costs one queued review some
/// latency. Retrying too early spends another request against an exhausted quota, which on some
/// plans extends the limit — so the two directions are not symmetric and the policy is not
/// "closest guess".</para>
/// </remarks>
public static partial class CooldownParser
{
    /// <summary>What to wait when the vendor said nothing usable.</summary>
    public static readonly TimeSpan Unparseable = TimeSpan.FromMinutes(30);

    /// <summary>The longest this will ever hold an account back on its own.</summary>
    /// <remarks>
    /// Five hours is the rolling window these plans actually use, so a longer guess would be holding
    /// an account that the vendor has already forgiven. A vendor that NAMES a longer time is
    /// believed — this ceiling is on the guess, not on the vendor's own answer.
    /// </remarks>
    public static readonly TimeSpan MaxGuess = TimeSpan.FromHours(5);

    /// <summary>A limit described as weekly, with no date to work from.</summary>
    /// <remarks>
    /// A day, not a week. The vendor said "weekly" but not WHEN the week turns, so the honest range is
    /// anywhere from minutes to seven days — and a seven-day guess would park a paid account for a
    /// week over a sentence, which is the one outcome worse than retrying slightly early. A day is the
    /// smallest wait that can plausibly cross a reset boundary, and the next refusal doubles from
    /// there anyway. (local, code round, asking why this number.)
    /// </remarks>
    public static readonly TimeSpan WeeklyWithoutDate = TimeSpan.FromHours(24);

    /// <summary>
    /// When this account may be used again.
    /// </summary>
    /// <param name="reason">The vendor's own text, as captured from stdout or stderr.</param>
    /// <param name="consecutive">
    /// How many times in a row this account has been rate-limited. Only used by the fallback, which
    /// doubles — a vendor that named a time is believed regardless of how often it has said so.
    /// </param>
    public static DateTimeOffset Until(string reason, DateTimeOffset nowUtc, int consecutive = 0)
    {
        var text = reason ?? string.Empty;

        return NamedInstant(text, nowUtc) ?? nowUtc + Fallback(text, consecutive);
    }

    /// <summary>The wait to use when no instant could be read out of the text.</summary>
    public static TimeSpan Fallback(string reason, int consecutive)
    {
        if (Weekly().IsMatch(reason ?? string.Empty))
        {
            return WeeklyWithoutDate;
        }

        // Doubling, because the same 30 minutes repeated is not a back-off — an account that has
        // been refused three times running is not thirty minutes from being ready.
        var doubled = Unparseable * Math.Pow(2, Math.Clamp(consecutive, 0, 8));

        return doubled > MaxGuess ? MaxGuess : doubled;
    }

    /// <summary>An instant the vendor NAMED, or null when it named none.</summary>
    private static DateTimeOffset? NamedInstant(string text, DateTimeOffset nowUtc)
    {
        if (OnDate().Match(text) is { Success: true } date
            && DateTime.TryParse(date.Groups["d"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            // "will refresh on July 15, 2026" — a date with no time of day. Midnight would be a
            // guess in the short direction, so the whole named day is waited out.
            return new DateTimeOffset(parsed.Date.AddDays(1), TimeSpan.Zero);
        }

        return ClockTime().Match(text) is { Success: true } clock ? FromClock(clock, nowUtc) : null;
    }

    /// <summary>A time of day, resolved to the next instant that matches it.</summary>
    private static DateTimeOffset? FromClock(Match clock, DateTimeOffset nowUtc)
    {
        if (!TimeOnly.TryParse(clock.Groups["t"].Value.Replace(" ", string.Empty),
                CultureInfo.InvariantCulture, out var time))
        {
            return null;
        }

        var zone = clock.Groups["z"].Value;
        var offset = OffsetOf(zone);
        // The date in the VENDOR's zone, not ours: "9:30pm PT" is a wall-clock time over there, and
        // taking our own UTC date would land on the wrong day for most of the evening. Unspecified
        // kind because DateTimeOffset refuses a Utc-kind DateTime with a non-zero offset — which is
        // exactly what a zoned line produces, and what this test caught.
        var localDate = DateTime.SpecifyKind(nowUtc.ToOffset(offset).Date, DateTimeKind.Unspecified);
        var candidate = new DateTimeOffset(localDate + time.ToTimeSpan(), offset).ToUniversalTime();
        // A time already past is tomorrow's: "resets 9:30pm" read at 10pm means 9:30pm TOMORROW.
        var resolved = candidate <= nowUtc ? candidate.AddDays(1) : candidate;

        // When the vendor named no zone, the reading could be up to a day out in either direction,
        // and one of those directions is retrying into a live limit. So an unzoned answer is never
        // allowed to be SHORTER than the unparseable fallback.
        return zone.Length == 0 && resolved - nowUtc < Unparseable ? nowUtc + Unparseable : resolved;
    }

    /// <summary>
    /// The offset a named zone means, defaulting to UTC.
    /// </summary>
    /// <remarks>
    /// Only zones seen in these CLIs' output, with their fixed offsets. Not
    /// <c>TimeZoneInfo.FindSystemTimeZoneById</c>: the container is a chiselled image with no tz
    /// database, so that call throws there and works on a developer's box — the worst possible
    /// split. An abbreviation is ambiguous anyway (PT is -8 or -7 by season); the daylight case is
    /// one hour, and <see cref="FromClock"/> only ever rounds the wait UP.
    /// </remarks>
    private static TimeSpan OffsetOf(string zone) =>
        zone.ToUpperInvariant() switch
        {
            "PT" or "PST" => TimeSpan.FromHours(-8),
            "PDT" => TimeSpan.FromHours(-7),
            "ET" or "EST" => TimeSpan.FromHours(-5),
            "EDT" => TimeSpan.FromHours(-4),
            _ => TimeSpan.Zero,
        };

    // Captured 2026-09-05 on the VM, codex:
    //   "You've hit your session limit · resets 9:30pm (UTC)"
    // and the same shape without a zone, and claude's "try again at 3:45pm".
    [GeneratedRegex(@"(?:resets|try again at|available again at)\s+(?:\w{3}\s+)?(?<t>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s*\(?(?<z>UTC|P[SD]?T|E[SD]?T)?\)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClockTime();

    // antigravity: "your limit will refresh on July 15, 2026".
    [GeneratedRegex(@"(?:refreshes?|will refresh|resets)\s+on\s+(?<d>[A-Za-z]+\s+\d{1,2},?\s+\d{4})",
        RegexOptions.IgnoreCase)]
    private static partial Regex OnDate();

    // "weekly limit reached" with no date attached — a day is the smallest wait that can help.
    [GeneratedRegex(@"weekly", RegexOptions.IgnoreCase)]
    private static partial Regex Weekly();
}
