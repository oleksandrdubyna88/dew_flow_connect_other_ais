using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The vendor's own rate-limit sentence, read into an instant — by the one parser both halves use.
/// </summary>
/// <remarks>
/// Moved here from <c>src_server/tests/SlotTests.cs</c> with the type itself (story 5.3b of
/// todo/PLAN_every_round_is_counted.md): <see cref="CooldownParser"/> lives in the shared runners library now, and its
/// tests live beside its other readers' — the ladder's and the ledger's. The Team server's own use of it (a slot parked
/// until the stated instant) stays tested where the server is, in <c>AStatedResetParksTheSlotTests</c>.
/// </remarks>
public sealed class CooldownParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheCapturedCodexLineIsUnderstood()
    {
        // Captured verbatim from the VM, 2026-09-05.
        var until = CooldownParser.Until("You've hit your session limit · resets 9:30pm (UTC)", Now);

        until.Should().Be(new DateTimeOffset(2026, 9, 6, 21, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ATimeAlreadyPastIsTomorrow()
    {
        var at2300 = new DateTimeOffset(2026, 9, 6, 23, 0, 0, TimeSpan.Zero);

        CooldownParser.Until("resets 9:30pm (UTC)", at2300)
            .Should().Be(new DateTimeOffset(2026, 9, 7, 21, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ANamedZoneIsHonoured()
    {
        // 9:30pm PT is 05:30 UTC the next day — reading it as UTC would have retried 8 hours early,
        // straight back into the limit. (Plan round.)
        CooldownParser.Until("resets 9:30pm PT", Now)
            .Should().Be(new DateTimeOffset(2026, 9, 7, 5, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AnUnzonedTimeIsNeverAllowedToWaitLessThanTheFallback()
    {
        // "resets 12:05" read at 12:00 UTC is five minutes — but if the vendor meant a zone it did
        // not name, five minutes is a retry into a live limit. Waiting too long costs latency;
        // retrying too early spends quota, so the two directions are not symmetric.
        var until = CooldownParser.Until("try again at 12:05", Now);

        (until - Now).Should().BeGreaterThanOrEqualTo(CooldownParser.Unparseable);
    }

    [Fact]
    public void ADateWithNoTimeWaitsOutTheWholeDay()
    {
        CooldownParser.Until("your limit will refresh on July 15, 2026", Now)
            .Should().Be(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void SayingNothingUsefulIsThirtyMinutes() =>
        CooldownParser.Until("rate limit exceeded", Now).Should().Be(Now + CooldownParser.Unparseable);

    [Fact]
    public void RepeatedRefusalsBackOffAndThenStop()
    {
        CooldownParser.Fallback("rate limited", 0).Should().Be(TimeSpan.FromMinutes(30));
        CooldownParser.Fallback("rate limited", 1).Should().Be(TimeSpan.FromHours(1));
        CooldownParser.Fallback("rate limited", 2).Should().Be(TimeSpan.FromHours(2));
        // Capped at the rolling window itself: a longer guess holds an account the vendor has
        // already forgiven.
        CooldownParser.Fallback("rate limited", 9).Should().Be(CooldownParser.MaxGuess);
    }

    [Fact]
    public void AWeeklyLimitWithNoDateWaitsADay() =>
        CooldownParser.Fallback("you have reached your weekly limit", 0).Should().Be(CooldownParser.WeeklyWithoutDate);

    // ── a stated DURATION (story 5.3a of todo/PLAN_every_round_is_counted.md) ───────────────────────────────────────
    // agy states how long, not when. Until this was read, the server parked an exhausted Gemini slot on its doubling
    // 30-minute guess and asked again into the refusal for the whole ~110 h of a stated reset.

    [Fact]
    public void CooldownParser_ReadsResetsIn88h49m9s()
    {
        // Captured verbatim 2026-10-10 (research/RESULTS_vendor_quota_gauges.md §3.1), the error id redacted there.
        const string Said = """AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.","status":"RESOURCE_EXHAUSTED","error_code":429,"code_kind":"http","retryable":true,"error_id":"<redacted>"}""";

        CooldownParser.Until(Said, Now).Should().Be(Now + new TimeSpan(88, 49, 9),
            "the vendor said how long; the 30-minute guess re-asks an exhausted account into the refusal");
    }

    [Fact]
    public void CooldownParser_ReadsResetsIn33m52s()
    {
        // The fixture src_mcp/tests/fixtures/antigravity/consult-quota.stderr.txt, verbatim.
        const string Said = """AGY_ERROR {"code":"RESOURCE_EXHAUSTED","message":"RESOURCE_EXHAUSTED (code 429): Individual quota reached for gemini-3.1-pro-high. Resets in 33m52s"}""";

        CooldownParser.Until(Said, Now).Should().Be(Now + new TimeSpan(0, 33, 52));
    }

    [Fact]
    public void ADurationOfOnePartIsReadToo()
    {
        CooldownParser.Until("Resets in 5h.", Now).Should().Be(Now + TimeSpan.FromHours(5));
        CooldownParser.Until("resets in 45s", Now).Should().Be(Now + TimeSpan.FromSeconds(45));
        CooldownParser.Until("Resets in 12m", Now).Should().Be(Now + TimeSpan.FromMinutes(12));
    }

    [Fact]
    public void ADurationNoVendorWindowCouldMean_IsTheFallback_NeverAThrow()
    {
        // The plan round's finding: a malformed refusal must not fail the job. The vendor DID state a wait, so it is not
        // "nothing usable": the guess errs long, to MaxGuess (CodeRabbit on #754) — never the 30-minute retry into it.
        CooldownParser.Until("Resets in 999999999999999999h", Now).Should().Be(Now + CooldownParser.MaxGuess);
        CooldownParser.Until("Resets in 9999h", Now).Should().Be(Now + CooldownParser.MaxGuess,
            "four hundred days is not a window, it is a parse of something else");
        CooldownParser.Until("Resets in 336h", Now).Should().Be(Now + CooldownParser.MaxStatedWait, "exactly the ceiling is believed");
    }

    [Fact]
    public void ADurationWithTextGluedToItsUnit_IsNoDuration()
    {
        // The code round's finding: a reworded or truncated line must not read as a SHORT wait — "2seconds" read as
        // two seconds would put the slot straight back into the refusal. A stated wait that cannot be read errs long.
        CooldownParser.Until("Resets in 2seconds", Now).Should().Be(Now + CooldownParser.MaxGuess);
        CooldownParser.Until("Resets in 1hgarbage", Now).Should().Be(Now + CooldownParser.MaxGuess);
        CooldownParser.Until("Resets in 88h49m9s.", Now).Should().Be(Now + new TimeSpan(88, 49, 9), "a sentence's full stop is a boundary");
    }

    [Fact]
    public void ASpacedDuration_IsReadWhole_NeverAsItsFirstPart()
    {
        // CodeRabbit on #754: "Resets in 2h 30m" read as two hours retries thirty minutes into the refusal. The spaced
        // form is read whole; a form that cannot be read whole is not read in part — it errs long, to MaxGuess.
        CooldownParser.Until("Resets in 2h 30m", Now).Should().Be(Now + new TimeSpan(2, 30, 0));
        CooldownParser.Until("Resets in 1h 2m 3s.", Now).Should().Be(Now + new TimeSpan(1, 2, 3));
        CooldownParser.Until("Resets in 2h 30 minutes", Now).Should().Be(Now + CooldownParser.MaxGuess,
            "the first part alone is a SHORTER wait than the one stated");
        // CodeRabbit on #754 again: a punctuation separator is the same partial match — "2h" read alone would end the
        // cooldown 30 minutes before the stated reset.
        CooldownParser.Until("Resets in 2h, 30m", Now).Should().Be(Now + CooldownParser.MaxGuess,
            "a comma-separated duration read as its first part is a SHORTER wait than the one stated");
        CooldownParser.Until("Resets in 2h; 30m", Now).Should().Be(Now + CooldownParser.MaxGuess);
        CooldownParser.Until("Resets in 2h, then try again", Now).Should().Be(Now + TimeSpan.FromHours(2),
            "a comma that is not followed by more of the duration ends it");
    }

    [Fact]
    public void AStatedWaitInWords_ErrsLong_AndWeeklyStillWinsWhenLonger()
    {
        // CodeRabbit on #754: a wait stated in words is still a stated wait — never the 30-minute retry into it.
        CooldownParser.Until("Resets in one hour", Now).Should().Be(Now + CooldownParser.MaxGuess);
        // And "weekly" is checked first because it is the LONGER guess (a day): both err long, the longer wins.
        CooldownParser.Until("weekly limit reached. Resets in 9999h", Now).Should().Be(Now + CooldownParser.WeeklyWithoutDate);
    }

    // ── the STATED reset alone (story 5.3b of todo/PLAN_every_round_is_counted.md) ──────────────────────────────────
    // What the local half reads: the instant a vendor named, and nothing when it named none — never the guess `Until`
    // parks a slot on, because a guess written into the ledger or shown on a card would be a claim the vendor never made.

    [Fact]
    public void AStatedReset_IsTheInstantTheVendorNamed()
    {
        const string Agy = "error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.";

        CooldownParser.StatedReset(Agy, Now).Should().Be(Now + new TimeSpan(88, 49, 9), "agy's verbatim line states how long");
        CooldownParser.StatedReset("Resets in 33m52s", Now).Should().Be(Now + new TimeSpan(0, 33, 52));
        CooldownParser.StatedReset("resets 9:30pm (UTC)", Now).Should().Be(new DateTimeOffset(2026, 9, 6, 21, 30, 0, TimeSpan.Zero),
            "a clock time is stated too, and read exactly as the server parks on it");
        CooldownParser.StatedReset("your limit will refresh on July 15, 2026", Now).Should().Be(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NoStatedReset_IsNone_NeverTheGuess()
    {
        CooldownParser.StatedReset("rate limit exceeded", Now).Should().BeNull("the vendor named no time; the 30-minute guess is the server's policy, not a statement");
        CooldownParser.StatedReset("you have reached your weekly limit", Now).Should().BeNull("weekly with no date names no instant");
        CooldownParser.StatedReset("Resets in one hour", Now).Should().BeNull("a wait in words cannot be read, so nothing is claimed");
        CooldownParser.StatedReset("Resets in 9999h", Now).Should().BeNull("past the ceiling a duration is a parse of something else");
        CooldownParser.StatedReset(string.Empty, Now).Should().BeNull();
    }

    [Fact]
    public void ADurationIsNotMistakenForAClockTime()
    {
        // "resets" is the word the clock-time form starts with; "in" must keep the two apart.
        CooldownParser.Until("Resets in 9h30m", Now).Should().Be(Now + new TimeSpan(9, 30, 0));
        CooldownParser.Until("resets 9:30pm (UTC)", Now).Should().Be(new DateTimeOffset(2026, 9, 6, 21, 30, 0, TimeSpan.Zero));
    }
}
