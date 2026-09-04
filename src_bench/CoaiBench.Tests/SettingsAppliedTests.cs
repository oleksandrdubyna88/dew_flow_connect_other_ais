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
    private static JsonObject Config(int rounds, int threshold, string onExhausted) =>
        JsonNode.Parse($$"""
            { "roles": { "PlanCritique": { "maxRounds": {{rounds}}, "threshold": {{threshold}} } },
              "onExhausted": "{{onExhausted}}" }
            """) as JsonObject ?? [];

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

    // ---------- the three switches, which are visible as ORDERS ----------

    [Fact]
    public void ASwitchThatIsOn_MustProduceItsOrder()
    {
        var applied = SettingsCheck.Of(
            Asked(("COAI_AUTONOMOUS", "true"), ("COAI_SPLIT_PLAN", "true"), ("COAI_SPLIT_WITH_FABLE", "true")),
            null,
            [Plan("proceed",
                "Split this plan into 2-4 EPICS … After EVERY story: call review_code …",
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
            [Plan("proceed", "Split this plan into 2-4 EPICS …")]);

        applied.Ok.Should().BeFalse();
        applied.Mismatches.Should().ContainSingle().Which.Should().Contain("Fable");
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
