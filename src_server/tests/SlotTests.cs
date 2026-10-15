using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>The pure half of the slot machinery: what to launch with, and which account to pick.</summary>
/// <remarks>
/// <c>CooldownParserTests</c> lived here until story 5.3b of todo/PLAN_every_round_is_counted.md moved
/// <c>CooldownParser</c> to the shared runners library; its tests moved with it, to
/// <c>src_mcp/tests/CooldownParserTests.cs</c>.
/// </remarks>
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

    /// <summary>Story 6.1a of todo/PLAN_every_round_is_counted.md: unavailable until recovery proves it safe.</summary>
    [Fact]
    public void ABlockedAccountIsNeverPicked_WhateverTheClockSays_AndTheRefusalNamesTheWrongCureAway()
    {
        var blocked = Slot("a") with { Blocked = BlockedReasons.ReaderIdentityMissing };

        SlotSelector.Pick([blocked], Now).Should().BeNull();
        SlotSelector.Pick([blocked], Now.AddDays(365)).Should().BeNull("no clock clears it");
        SlotSelector.Pick([blocked, Slot("b", Now.AddHours(-1))], Now)!.Name.Should().Be("b", "another account still takes the job");

        var sentence = SlotSelector.Explain([blocked], Now);
        sentence.Should().Contain("unavailable").And.Contain("reader");
        sentence.Should().NotContain("login", "its credentials are fine, and a sign-in meets the same barrier");
    }
}
