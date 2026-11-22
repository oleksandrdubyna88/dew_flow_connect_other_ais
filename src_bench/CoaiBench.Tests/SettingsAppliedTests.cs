using System.Text.Json.Nodes;
using Xunit;
using FluentAssertions;
using CoaiBench.Model;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// The bench checks that the settings it asked for are the ones the run actually got.
/// </summary>
/// <remarks>
/// Asked for after a campaign produced its numbers on `maxRounds 3, threshold 2, onExhausted Human`
/// while the operator's configuration said 1, 6 and good_enough — and nothing in the output said so.
/// A setting that is accepted and does nothing looks exactly like one that works.
/// </remarks>
public sealed class SettingsAppliedTests
{
    /// <param name="autonomous">The session's <c>autonomous</c> member; null writes none, as a server from before policy auto left it.</param>
    private static JsonObject Config(int rounds, int threshold, string onExhausted, bool? autonomous = null) =>
        JsonNode.Parse($$"""
            { "roles": { "PlanCritique": { "maxRounds": {{rounds}}, "threshold": {{threshold}} } },
              "onExhausted": "{{onExhausted}}"{{Member(autonomous)}} }
            """) as JsonObject ?? [];

    private static string Member(bool? autonomous) =>
        autonomous is { } marker ? ", \"autonomous\": " + (marker ? "true" : "false") : string.Empty;

    private static Dictionary<string, string> Asked(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static StageResult Plan(string verdict, params string[] commands) =>
        new("plan-1", 30, Verdict: verdict) { Commands = commands };

    [Fact]
    public void WhatWasAskedFor_AndWhatTheSessionGot_Agreeing()
    {
        var applied = SettingsCheck.Of(
            Asked(("COAI_ROUNDS_PLANCRITIQUE", "1"), ("COAI_THRESHOLD_PLANCRITIQUE", "6"),
                  ("COAI_ON_EXHAUSTED", "good_enough")),
            Config(1, 6, "GoodEnough"),
            [Plan("good_enough")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().Contain("on-exhausted");
    }

    [Fact]
    public void AndTheCampaignThatCostAnEvening()
    {
        // The exact numbers: asked 1/6/good_enough, got the defaults, and every table was about a
        // machine nobody runs.
        var applied = SettingsCheck.Of(
            Asked(("COAI_ROUNDS_PLANCRITIQUE", "1"), ("COAI_THRESHOLD_PLANCRITIQUE", "6"),
                  ("COAI_ON_EXHAUSTED", "good_enough")),
            Config(3, 2, "Human"),
            [Plan("revise")]);

        applied.Ok.Should().BeFalse();
        applied.Mismatches.Should().HaveCount(3);
        applied.Mismatches.Should().Contain(m => m.Contains("rounds asked 1") && m.Contains("says 3"));
        applied.Mismatches.Should().Contain(m => m.Contains("threshold asked 6") && m.Contains("says 2"));
        applied.Mismatches.Should().Contain(m => m.Contains("good_enough") && m.Contains("Human"));
    }

    [Fact]
    public void GoodEnoughAndGood_Enough_AreTheSameDecisionSpelledTwice() =>
        SettingsCheck.Of(Asked(("COAI_ON_EXHAUSTED", "good_enough")), Config(1, 6, "GoodEnough"), [])
            .Ok.Should().BeTrue();

    // ---------- policy auto: the word AND the marker ----------

    /// <summary>
    /// Policy auto is stored as <c>GoodEnough</c> with <c>autonomous: true</c>; the word alone cannot tell it from a named <c>good_enough</c>.
    /// A session with no <c>autonomous</c> member is an older server's, and means the policy it names.
    /// </summary>
    [Theory]
    [InlineData("auto", "GoodEnough", true, true)]
    [InlineData("AUTO", "GoodEnough", true, true)]
    [InlineData("auto", "GoodEnough", false, false)]
    [InlineData("auto", "GoodEnough", null, false)]
    [InlineData("auto", "Human", null, false)]
    [InlineData("good_enough", "GoodEnough", false, true)]
    [InlineData("good_enough", "GoodEnough", null, true)]
    [InlineData("good_enough", "GoodEnough", true, false)]
    [InlineData("human", "Human", false, true)]
    [InlineData("human", "Human", null, true)]
    [InlineData("human", "Human", true, false)]
    [InlineData("human", "GoodEnough", true, false)]
    [InlineData("continue", "Continue", false, true)]
    [InlineData("escalate", "Escalate", null, true)]
    [InlineData("escalate", "Escalate", true, false)]
    public void TheAskedPolicy_AgainstWhatTheSessionStored(string asked, string seen, bool? autonomous, bool ok)
    {
        var applied = SettingsCheck.Of(Asked(("COAI_ON_EXHAUSTED", asked)), Config(1, 6, seen, autonomous), []);

        applied.Ok.Should().Be(ok, string.Join("; ", applied.Mismatches));
        applied.Checked.Should().Contain("on-exhausted");
        if (!ok)
        {
            applied.Mismatches.Should().ContainSingle().Which.Should().Contain($"asked '{asked}'").And.Contain($"session says '{seen}'");
        }
    }

    [Fact]
    public void ASessionThatRunsAuto_IsNotJudgedWhenNothingWasAsked()
    {
        var applied = SettingsCheck.Of(Asked(), Config(1, 6, "GoodEnough", autonomous: true), []);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().NotContain("on-exhausted");
    }

    // ---------- the three switches, which are visible as ORDERS ----------

    [Fact]
    public void ASwitchThatIsOn_MustProduceItsOrder()
    {
        var applied = SettingsCheck.Of(
            Asked(("COAI_AUTONOMOUS", "true"), ("COAI_SPLIT_PLAN", "true"), ("COAI_SPLIT_WITH_FABLE", "true")),
            null,
            [Plan("proceed",
                "Split this plan into 2-3 EPICS, each of 2-3 logically complete STORIES … THE GATE runs once per EPIC …",
                "Do the SPLIT itself with Fable at its highest available version …",
                "Work AUTONOMOUSLY. A question that does not block you …")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().Contain("COAI_SPLIT_WITH_FABLE");
    }

    [Fact]
    public void ASwitchThatIsOnAndSilent_IsTheDefectThisExistsFor()
    {
        // Split with Fable shipped doing nothing for a release: the box was ticked, the order was
        // withheld, and every signal said the feature was on.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_WITH_FABLE", "true")),
            null,
            [Plan("proceed", "Split this plan into 2-3 EPICS, each of 2-3 logically complete STORIES …")]);

        applied.Ok.Should().BeFalse();
        applied.Mismatches.Should().ContainSingle().Which.Should().Contain("COAI_SPLIT_WITH_FABLE");
    }

    [Fact]
    public void AnOrderToBuildItAsItStands_IsStillTheSplitSwitchApplied()
    {
        // Issue #131: the smallest size says "small enough to build as it stands", which carries
        // neither "Split this plan into" nor "already under way" — only the gate ending every split
        // order carries. Without it a short plan read as a switch that did nothing.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_PLAN", "true")),
            null,
            [Plan("proceed", "This plan is small enough to build as it stands … THE GATE runs once for this work …")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().Contain("COAI_SPLIT_PLAN");
    }

    [Fact]
    public void TheModelOrder_IsRecognisedWhateverModelsItNames()
    {
        // Issue #117 made the two models a per-caller choice, so a Codex caller's order names no
        // Fable at all. The check reads the words every variant opens with, never a model name — or
        // every round a Codex session ran would be reported as a switch that did nothing.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_PLAN", "true"), ("COAI_SPLIT_WITH_FABLE", "true")),
            null,
            [Plan("proceed",
                "Split this plan into 2-3 EPICS, each of 2-3 logically complete STORIES …",
                "Do the SPLIT itself with gpt-6-astra at its highest available version …")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().Contain("COAI_SPLIT_WITH_FABLE");
    }

    [Fact]
    public void TheAutonomyOrderIsNotEvidenceThatSplittingIsOn()
    {
        // Found by the campaign of 2026-09-04. The autonomy order says "re-read every epic and
        // STORY you have written so far", so a check for the word `story` passed off a sentence
        // about something else entirely, and COAI_SPLIT_PLAN was reported working in a run where
        // no split order was given at all. A word that appears in another order is not a check.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_PLAN", "true")),
            null,
            [Plan("good_enough", "Work AUTONOMOUSLY. … re-read every epic and story you have written so far …")]);

        applied.Ok.Should().BeFalse("nothing in that round ordered a split");
    }

    [Fact]
    public void TheAlreadySplitOrder_IsTheSwitchWorking_NotTheSwitchMissing()
    {
        // The second run of a campaign, and every real epic coming back for its own plan review: the
        // split order is given once per calling session, so what arrives is the order NOT to split
        // again. That order only exists because the switch is on. Reading it as a failure is how
        // three of four runs came back marked SETTINGS NOT APPLIED while the feature worked.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_PLAN", "true")),
            null,
            [Plan("good_enough", "This plan is a PIECE of a split that is already under way, so do NOT split it again: …")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().Contain("COAI_SPLIT_PLAN");
    }

    [Fact]
    public void FableIsUncheckedWhenThereWasNoSplitToDoWithIt()
    {
        // Fable's order rides on the split order and cannot appear without it. When the round said
        // "already split", the absence of a Fable order is not evidence about the switch — and a
        // measuring instrument reports the absence of evidence as unchecked, never as a failure.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_PLAN", "true"), ("COAI_SPLIT_WITH_FABLE", "true")),
            null,
            [Plan("good_enough", "This plan is a PIECE of a split that is already under way, so do NOT split it again: …")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().NotContain("COAI_SPLIT_WITH_FABLE");
    }

    [Fact]
    public void ASwitchThatIsOff_IsNotChecked() =>
        SettingsCheck.Of(Asked(("COAI_AUTONOMOUS", "false")), null, [Plan("proceed")])
            .Checked.Should().BeEmpty();

    [Fact]
    public void APlanThatNeverPassed_ProvesNothingEitherWay()
    {
        // The order to build follows permission to build, so a `revise` round carries no commands
        // and their absence is not evidence.
        var applied = SettingsCheck.Of(
            Asked(("COAI_SPLIT_PLAN", "true")), null, [Plan("revise")]);

        applied.Ok.Should().BeTrue();
        applied.Checked.Should().BeEmpty();
    }

    [Fact]
    public void NoSessionFile_ChecksNothingRatherThanPassingEverything() =>
        SettingsCheck.Of(Asked(("COAI_ROUNDS_PLANCRITIQUE", "1")), null, []).Checked.Should().BeEmpty();
}
