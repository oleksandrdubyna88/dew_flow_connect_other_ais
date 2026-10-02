using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The gate behind <c>ask_human</c> (<c>todo/PLAN_question_consultant.md</c> D7–D9, D14 (a), S3 acceptance 1): pure,
/// so the whole table — phase × mode × productionRisk × proof × availability × readability — is a test and never a
/// screen somebody reads.
/// </summary>
/// <remarks>
/// A wrong branch here either interrupts the person on every question or hands the AI its own bypass; that is
/// why every row of the table is written out rather than sampled.
/// </remarks>
public sealed class AskGateTests
{
    private static AskGateInput Building(QuestionMode mode = QuestionMode.Require, int batches = 0) =>
        new() { Mode = mode, Phase = AskPhase.Building, BatchesAsked = batches };

    private static AskDecision.Allowed Allowed(AskGateInput input) =>
        AskGate.Decide(input).Should().BeOfType<AskDecision.Allowed>().Subject;

    private static AskDecision.Refused Refused(AskGateInput input) =>
        AskGate.Decide(input).Should().BeOfType<AskDecision.Refused>().Subject;

    private static readonly QuestionMode[] EveryMode = [QuestionMode.Off, QuestionMode.Remind, QuestionMode.Require];

    [Fact]
    public void ThePolicyIsTwoFreeBatches_AndTheInputDefaultsToIt()
    {
        QuestionPolicy.FreeBatches.Should().Be(2, "A7: the first two batches after the plan's proceed go to the person");
        new AskGateInput().FreeBatches.Should().Be(QuestionPolicy.FreeBatches);
    }

    [Fact]
    public void ThePlanStage_GoesToThePerson_InEveryMode_AndCountsNothing()
    {
        foreach (var mode in EveryMode)
        {
            var allowed = Allowed(Building(mode) with { Phase = AskPhase.Planning, BatchesAsked = 7 });

            allowed.Counted.Should().BeFalse("batches are counted from the plan's proceed, not before it");
            allowed.ConsultBeside.Should().BeFalse();
            allowed.Note.Should().BeEmpty($"the person is the right door while the plan is formed ({mode})");
        }
    }

    [Fact]
    public void AfterTheRelease_ThePersonAgain_InEveryMode()
    {
        foreach (var mode in EveryMode)
        {
            var allowed = Allowed(Building(mode, batches: 9) with { Phase = AskPhase.Released });

            allowed.Counted.Should().BeFalse();
            allowed.Note.Should().BeEmpty($"after the release questions go to the person directly again ({mode})");
        }
    }

    [Fact]
    public void TheFirstTwoBatchesAfterProceed_GoToThePerson_AndAreCounted()
    {
        var first = Allowed(Building(batches: 0));
        var second = Allowed(Building(batches: 1));

        first.Counted.Should().BeTrue("the batch is what the next question is judged by");
        second.Counted.Should().BeTrue();
        first.Note.Should().Contain("free batch 1 of 2").And.Contain("ask_consultants",
            "the AI is told where it stands, so the refusal on the third does not arrive as a surprise");
        second.Note.Should().Contain("free batch 2 of 2");
    }

    [Fact]
    public void TheThirdBatchUnderRequire_IsRefusedNamingAskConsultants()
    {
        var refused = Refused(Building(batches: 2));

        refused.Sentence.Should().Contain("ask_consultants", "the cure is the other tool, named")
            .And.Contain("consultId", "and how its answer comes back through this door")
            .And.Contain("productionRisk", "and the one bypass, spelled as the operator spelled it")
            .And.Contain("2", "the count the refusal rests on is shown, so the AI can argue with it");
    }

    [Fact]
    public void TheThirdBatchUnderRemind_IsAllowedWithTheNote()
    {
        var allowed = Allowed(Building(QuestionMode.Remind, batches: 2));

        allowed.Counted.Should().BeTrue();
        allowed.Note.Should().Contain("ask_consultants").And.ContainEquivalentOf("remind",
            "remind never refuses the work; it says what require would have refused");
    }

    [Fact]
    public void Off_IsSilent_WhateverTheCount()
    {
        var allowed = Allowed(Building(QuestionMode.Off, batches: 5));

        allowed.Note.Should().BeEmpty("a switch nobody set must change nothing a caller reads");
        allowed.Counted.Should().BeFalse("and count nothing — the phase is the gate's business, and the gate is off");
        allowed.ConsultBeside.Should().BeFalse();
    }

    [Fact]
    public void AVerifiedConsultId_OpensTheDoor_AndItsNoteTravels()
    {
        var allowed = Allowed(Building(batches: 2) with { Proof = new ConsultProof.Verified("abc", "the consultants answered") });

        allowed.Counted.Should().BeTrue("the person is asked, so it is a batch");
        allowed.Note.Should().Be("the consultants answered");
    }

    [Fact]
    public void AConsultThatCouldNotBeHad_StandsTheGateDown_ThroughItsVerifiedRecord()
    {
        // The coordinator's decision for S3: a record that ended `failed` (no row answered), `quota_spent` or
        // `none_available` is a consultant that could not be had — D9's stand-down, said in the note.
        var allowed = Allowed(Building(batches: 2) with { Proof = new ConsultProof.Verified("abc", "stood down: no consultant answered") });

        allowed.Note.Should().Contain("stood down");
    }

    [Fact]
    public void ARejectedConsultId_IsAsIfNoneWereGiven_AndTheSentenceSaysWhy()
    {
        var rejected = new ConsultProof.Rejected("abc", "it is not a question this caller session asked in this repository");

        Refused(Building(batches: 2) with { Proof = rejected }).Sentence
            .Should().Contain("ask_consultants").And.Contain(rejected.Why, "D14 (a): refused as if none were given, and told why the id did not count");
        Allowed(Building(QuestionMode.Remind, batches: 2) with { Proof = rejected }).Note.Should().Contain(rejected.Why);
        Allowed(Building(batches: 0) with { Proof = rejected }).Note.Should().Contain(rejected.Why,
            "a free batch is allowed, and the bad id is still named so the next call does not repeat it");
    }

    [Fact]
    public void ProductionRiskWithoutAReason_IsRefused_InEveryMode()
    {
        foreach (var mode in EveryMode)
        {
            Refused(Building(mode) with { ProductionRisk = true, RiskReason = "  " }).Sentence
                .Should().Contain("riskReason", $"the bypass is a claim, and a claim with no reason is refused ({mode})");
        }
    }

    [Fact]
    public void AProductionRisk_AsksThePersonAtOnce_AndRunsTheConsultantsBeside()
    {
        var allowed = Allowed(Building(batches: 2) with { ProductionRisk = true, RiskReason = "the migration drops a column" });

        allowed.Counted.Should().BeTrue();
        allowed.ConsultBeside.Should().BeTrue("D8: the consultants still run, in the background, their answers folded under the card");
        allowed.Note.Should().ContainEquivalentOf("production risk").And.Contain("beside");
    }

    [Fact]
    public void AProductionRisk_WhenNoConsultantCanBeHad_StillAsksThePerson_AndSaysNobodyRanBeside()
    {
        var allowed = Allowed(Building(batches: 2) with
        {
            ProductionRisk = true,
            RiskReason = "the migration drops a column",
            Unavailable = "no question-consultant row is switched on",
        });

        allowed.ConsultBeside.Should().BeFalse();
        allowed.Note.Should().Contain("no question-consultant row is switched on");
    }

    [Fact]
    public void AProductionRisk_InTheFreePhase_IsCountedLikeAnyOtherBatch_AndStillRunsThemBeside()
    {
        var allowed = Allowed(Building(batches: 0) with { ProductionRisk = true, RiskReason = "a rollback could not be undone" });

        allowed.Counted.Should().BeTrue();
        allowed.ConsultBeside.Should().BeTrue("the AI asked for the consultants beside; a free batch is no reason to withhold them");
    }

    [Fact]
    public void NoConsultantCanBeHad_TheGateStandsDown_WithTheNote()
    {
        foreach (var why in new[] { "the question consultant is switched off", "no question-consultant row is switched on", "this caller session has asked the consultants 10 questions" })
        {
            var allowed = Allowed(Building(batches: 2) with { Unavailable = why });

            allowed.Counted.Should().BeTrue();
            allowed.Note.Should().Contain("stood down").And.Contain(why, "D9: never a deadlock, and the reason is said");
        }
    }

    [Fact]
    public void AnUnreadablePhaseStore_Allows_AndSaysSo()
    {
        var allowed = Allowed(Building(batches: 0) with { PhaseUnreadable = true });

        allowed.Note.Should().Contain("could not be read");
        allowed.Counted.Should().BeFalse("nothing is written over a record that could not be read");
    }

    [Fact]
    public void ReleasedOrPlanning_OutranksEverythingElse_NothingIsRefused()
    {
        foreach (var phase in new[] { AskPhase.Planning, AskPhase.Released })
        {
            AskGate.Decide(Building(batches: 9) with { Phase = phase, Proof = new ConsultProof.Rejected("x", "bad"), Unavailable = "off" })
                .Should().BeOfType<AskDecision.Allowed>();
        }
    }

    [Fact]
    public void NothingTheGateSays_EverSaysCritical()
    {
        var everything = new List<string>();
        foreach (var mode in EveryMode)
        {
            foreach (var phase in new[] { AskPhase.Planning, AskPhase.Building, AskPhase.Released })
            {
                foreach (var batches in new[] { 0, 1, 2, 5 })
                {
                    foreach (var proof in new ConsultProof[] { ConsultProof.Nothing, new ConsultProof.Verified("a", "n"), new ConsultProof.Rejected("a", "why") })
                    {
                        foreach (var risk in new[] { false, true })
                        {
                            foreach (var unavailable in new[] { string.Empty, "no row is on" })
                            {
                                everything.Add(Text(AskGate.Decide(new AskGateInput
                                {
                                    Mode = mode,
                                    Phase = phase,
                                    BatchesAsked = batches,
                                    ProductionRisk = risk,
                                    RiskReason = risk ? "a reason" : string.Empty,
                                    Proof = proof,
                                    Unavailable = unavailable,
                                })));
                            }
                        }
                    }
                }
            }
        }

        everything.Should().HaveCount(3 * 3 * 4 * 3 * 2 * 2);
        everything.Should().AllSatisfy(text => text.Should().NotContainEquivalentOf("critical",
            "A8: the canonical invented severity, forbidden in everything a caller reads"));
    }

    private static string Text(AskDecision decision) => decision switch
    {
        AskDecision.Allowed allowed => allowed.Note,
        AskDecision.Refused refused => refused.Sentence,
        _ => throw new InvalidOperationException("the union is closed"),
    };
}
