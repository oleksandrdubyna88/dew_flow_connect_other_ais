using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>The pure half of the slot machinery: what to launch with, and which account to pick.</summary>
public sealed class SlotEnvironmentTests
{
    [Fact]
    public void HomeAndTheXdgDirectoriesAllPointIntoTheSlot()
    {
        var env = SlotEnvironment.For("antigravity", "/data/accounts/antigravity/a");

        // antigravity has no directory variable of its own, so these ARE its whole isolation. A CLI
        // that reads $XDG_CONFIG_HOME would otherwise share one config directory between accounts,
        // and the second sign-in would overwrite the first. (Plan round, Blocking.)
        env["HOME"].Should().Be("/data/accounts/antigravity/a");
        env["XDG_CONFIG_HOME"].Should().StartWith("/data/accounts/antigravity/a");
        env["XDG_DATA_HOME"].Should().StartWith("/data/accounts/antigravity/a");
        env["XDG_CACHE_HOME"].Should().StartWith("/data/accounts/antigravity/a");
        env["XDG_STATE_HOME"].Should().StartWith("/data/accounts/antigravity/a");
    }

    [Fact]
    public void TwoSlotsShareNoDirectoryAtAll()
    {
        var a = SlotEnvironment.For("codex", "/data/accounts/codex/a");
        var b = SlotEnvironment.For("codex", "/data/accounts/codex/b");

        a.Values.Should().NotIntersectWith(b.Values,
            "any shared path is a place where one account can overwrite the other's credentials");
    }

    [Fact]
    public void CodexGetsItsOwnHome() =>
        SlotEnvironment.For("codex", "/s")["CODEX_HOME"].Should().Contain(".codex");

    [Fact]
    public void ClaudeGetsItsConfigDirectoryAndNoTokenWhenThereIsNone()
    {
        var env = SlotEnvironment.For("claude", "/s");

        env["CLAUDE_CONFIG_DIR"].Should().Contain(".claude");
        env.Should().NotContainKey("CLAUDE_CODE_OAUTH_TOKEN");
    }

    [Fact]
    public void AClaudeTokenIsPassedAsAValueOrNotAtAll()
    {
        SlotEnvironment.For("claude", "/s", "sk-ant-oat01-example")["CLAUDE_CODE_OAUTH_TOKEN"]
            .Should().Be("sk-ant-oat01-example");
        // An empty token must not set the variable to nothing: a CLI reading an empty
        // CLAUDE_CODE_OAUTH_TOKEN treats it as a credential and fails differently from one that is
        // simply not set.
        SlotEnvironment.For("claude", "/s", string.Empty).Should().NotContainKey("CLAUDE_CODE_OAUTH_TOKEN");
    }
}

public sealed class SlotSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static AccountSlot Slot(
        string name, DateTimeOffset? lastUsed = null, DateTimeOffset? cooldown = null, bool needsSignIn = false) =>
        new("codex", name, "/d/" + name, lastUsed ?? DateTimeOffset.MinValue, cooldown, needsSignIn, "", 0);

    [Fact]
    public void TheLeastRecentlyUsedReadyAccountIsPicked()
    {
        var picked = SlotSelector.Pick(
            [Slot("a", Now.AddMinutes(-1)), Slot("b", Now.AddHours(-3)), Slot("c", Now.AddMinutes(-30))], Now);

        // First-listed would drive account 'a' into its rate limit while 'b' sat idle, which is the
        // opposite of why an operator adds a second account.
        picked!.Name.Should().Be("b");
    }

    [Fact]
    public void ACoolingAccountIsSkippedUntilItsTimePasses()
    {
        IReadOnlyList<AccountSlot> slots = [Slot("a", cooldown: Now.AddMinutes(10)), Slot("b", Now.AddHours(-1))];

        SlotSelector.Pick(slots, Now)!.Name.Should().Be("b");
        SlotSelector.Pick(slots, Now.AddMinutes(11))!.Name.Should().Be("a", "'a' is older, and its cooldown has passed");
    }

    [Fact]
    public void ASignedOutAccountIsNeverPickedNoMatterHowLongItWaits()
    {
        IReadOnlyList<AccountSlot> slots = [Slot("a", needsSignIn: true)];

        SlotSelector.Pick(slots, Now).Should().BeNull();
        SlotSelector.Pick(slots, Now.AddDays(365)).Should().BeNull("waiting never signs an account back in");
    }

    [Fact]
    public void TheRefusalSaysWhichCureApplies()
    {
        SlotSelector.Explain([Slot("a", cooldown: Now.AddMinutes(10))], Now)
            .Should().Contain("rate-limited").And.Contain("comes back at");

        SlotSelector.Explain([Slot("a", needsSignIn: true)], Now)
            .Should().Contain("login", "a signed-out account needs a human, and the message says so");

        SlotSelector.Explain([], Now).Should().Contain("no account slots");
    }
}

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
    }

    [Fact]
    public void ADurationIsNotMistakenForAClockTime()
    {
        // "resets" is the word the clock-time form starts with; "in" must keep the two apart.
        CooldownParser.Until("Resets in 9h30m", Now).Should().Be(Now + new TimeSpan(9, 30, 0));
        CooldownParser.Until("resets 9:30pm (UTC)", Now).Should().Be(new DateTimeOffset(2026, 9, 6, 21, 30, 0, TimeSpan.Zero));
    }
}
