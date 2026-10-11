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

    /// <summary>The longest stated duration that is believed — fourteen days.</summary>
    /// <remarks>
    /// Longer than any window these vendors state (the longest seen: a weekly window, and agy's 110 h). A duration past
    /// it — or one too large to be an instant at all — is a sentence with nothing usable in it, so the fallback applies
    /// rather than a throw that would fail the job (story 5.3a of todo/PLAN_every_round_is_counted.md, its plan round).
    /// </remarks>
    public static readonly TimeSpan MaxStatedWait = TimeSpan.FromDays(14);

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

        // The vendor stated a wait this could not read whole — a malformed number, one past MaxStatedWait, a form never
        // seen. A wait WAS stated, so the 30-minute retry would be the one direction certain to be wrong: the guess's
        // ceiling instead (CodeRabbit on #754; MaxStatedWait would park a paid account for a fortnight over one sentence).
        if (StatedButUnread().IsMatch(reason ?? string.Empty))
        {
            return MaxGuess;
        }

        // Doubling, because the same 30 minutes repeated is not a back-off — an account that has
        // been refused three times running is not thirty minutes from being ready.
        var doubled = Unparseable * Math.Pow(2, Math.Clamp(consecutive, 0, 8));

        return doubled > MaxGuess ? MaxGuess : doubled;
    }

    /// <summary>An instant the vendor NAMED, or null when it named none.</summary>
    private static DateTimeOffset? NamedInstant(string text, DateTimeOffset nowUtc)
    {
        // FIRST, so "resets in …" is never offered to the clock-time form, which also starts at "resets".
        if (StatedDuration(text) is { } wait)
        {
            return nowUtc + wait;
        }

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

    /// <summary>
    /// How long the vendor said to wait — <c>Resets in 88h49m9s</c> — or null when it said no usable duration.
    /// </summary>
    /// <remarks>
    /// Story 5.3a of todo/PLAN_every_round_is_counted.md. Believed like any named instant, so <see cref="MaxGuess"/> does
    /// not cap it; <see cref="MaxStatedWait"/> does, and anything past it — a number too large to be an instant at all
    /// included — is no duration, never a throw: a malformed refusal must not fail the job (its plan round).
    /// </remarks>
    private static TimeSpan? StatedDuration(string text)
    {
        var said = Duration().Match(text);
        if (!said.Success || said.Groups["h"].Length + said.Groups["m"].Length + said.Groups["s"].Length == 0)
        {
            return null;
        }

        var seconds = PartOf(said, "h") * 3600 + PartOf(said, "m") * 60 + PartOf(said, "s");

        return seconds <= MaxStatedWait.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
    }

    /// <summary>One part of a duration as a double — infinity for digits no long can hold, which the bound then refuses.</summary>
    private static double PartOf(Match said, string part) =>
        said.Groups[part].Length == 0 ? 0
        : double.TryParse(said.Groups[part].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value
        : double.PositiveInfinity;

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

    // Captured 2026-10-10 from agy 1.3.x on a Gemini quota refusal (research/RESULTS_vendor_quota_gauges.md §3.1):
    //   "… Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s."
    // and the fixture src_mcp/tests/fixtures/antigravity/consult-quota.stderr.txt: "… Resets in 33m52s".
    // The trailing boundary is the code round's: "2seconds" or "1hgarbage" must not read as a short wait. One space is
    // allowed inside ("2h 30m" is read whole), and a spaced number after the match refuses it (CodeRabbit on #754):
    // "2h 30 minutes" read as its first part would be a SHORTER wait than the one stated.
    [GeneratedRegex(@"resets\s+in\s+(?:(?<h>\d+)\s?h)?\s?(?:(?<m>\d+)\s?m)?\s?(?:(?<s>\d+)\s?s)?(?![A-Za-z0-9])(?!\s*\d)", RegexOptions.IgnoreCase)]
    private static partial Regex Duration();

    // A wait the vendor STATED that the duration form could not read whole — what makes the guess err long.
    [GeneratedRegex(@"resets\s+in\s+\d", RegexOptions.IgnoreCase)]
    private static partial Regex StatedButUnread();

    // "weekly limit reached" with no date attached — a day is the smallest wait that can help.
    [GeneratedRegex(@"weekly", RegexOptions.IgnoreCase)]
    private static partial Regex Weekly();
}
