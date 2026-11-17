using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The gate behind <c>ask_human</c> under policy auto (<c>todo/PLAN_auto_never_asks_a_person.md</c> § 4.4, owner decision O3;
/// <c>todo/PLAN_auto_never_asks_a_person_E1.md</c> § 9.1): the AI's own question goes to the consultants first, whatever the phase,
/// the mode and the free batches say; the person is asked only with a proof, or when no consultant can be had. Pure, like
/// <see cref="AskGateTests"/>, whose every row is the same gate with <c>Autonomous</c> false.
/// </summary>
/// <remarks>The input defaults are the ordinary conversation-door call in the building phase under <c>require</c>.</remarks>
public sealed class AskGateUnderAutoTests
{
    private const string Reason = "the migration drops a column";

    private static AskGateInput Auto(QuestionMode mode = QuestionMode.Require, AskPhase phase = AskPhase.Building, int batches = 0) =>
        new() { Autonomous = true, Mode = mode, Phase = phase, BatchesAsked = batches };

    private static AskDecision.Allowed Allowed(AskGateInput input) =>
        AskGate.Decide(input).Should().BeOfType<AskDecision.Allowed>().Subject;

    private static AskDecision.ConsultFirst ConsultFirst(AskGateInput input) =>
        AskGate.Decide(input).Should().BeOfType<AskDecision.ConsultFirst>().Subject;

    [Theory]
    [InlineData(QuestionMode.Off, AskPhase.Planning, 0, false)]
    [InlineData(QuestionMode.Off, AskPhase.Building, 0, false)]
    [InlineData(QuestionMode.Off, AskPhase.Released, 9, false)]
    [InlineData(QuestionMode.Remind, AskPhase.Planning, 7, false)]
    [InlineData(QuestionMode.Remind, AskPhase.Building, 0, false)]
    [InlineData(QuestionMode.Remind, AskPhase.Building, 5, false)]
    [InlineData(QuestionMode.Remind, AskPhase.Released, 0, false)]
    [InlineData(QuestionMode.Require, AskPhase.Planning, 0, false)]
    [InlineData(QuestionMode.Require, AskPhase.Building, 0, false)]
    [InlineData(QuestionMode.Require, AskPhase.Building, 1, false)]
    [InlineData(QuestionMode.Require, AskPhase.Building, 2, false)]
    [InlineData(QuestionMode.Require, AskPhase.Building, 9, false)]
    [InlineData(QuestionMode.Require, AskPhase.Released, 9, false)]
    [InlineData(QuestionMode.Require, AskPhase.Building, 0, true)]
    [InlineData(QuestionMode.Remind, AskPhase.Planning, 0, true)]
    public void TheAIsOwnQuestion_WithNoProof_IsConsultFirst(QuestionMode mode, AskPhase phase, int batches, bool phaseUnreadable)
    {
        var consult = ConsultFirst(Auto(mode, phase, batches) with { PhaseUnreadable = phaseUnreadable });

        consult.Note.Should().BeEmpty("no consultId was passed, so there is no rejected id to name");
    }

    [Fact]
    public void ARejectedProof_IsConsultFirst_AndSaysWhy()
    {
        var consult = ConsultFirst(Auto(batches: 0) with { Proof = new ConsultProof.Rejected("abc", "too old") });

        consult.Note.Should().Contain("abc").And.Contain("too old", "D14 (a): a consultId that did not count is named, with why");
        consult.Note.Should().Be(consult.Note.Trim(), "the note is the clause alone, without the leading space the sentences carry");
    }

    [Fact]
    public void AVerifiedProof_ReachesThePerson()
    {
        var verified = new ConsultProof.Verified("abc", "the consultants answered");

        var building = Allowed(Auto() with { Proof = verified });
        building.Counted.Should().BeTrue("the person is asked in the building phase, so the batch is recorded");
        building.ConsultBeside.Should().BeFalse();
        building.Note.Should().Be("the consultants answered");

        Allowed(Auto(phase: AskPhase.Planning) with { Proof = verified }).Counted.Should().BeFalse("batches are counted from the plan's proceed");
        Allowed(Auto(phase: AskPhase.Released) with { Proof = verified }).Counted.Should().BeFalse();
        Allowed(Auto(QuestionMode.Off) with { Proof = verified }).Counted.Should().BeFalse("a gate that is off records nothing");
        Allowed(Auto() with { Proof = verified, PhaseUnreadable = true }).Counted.Should().BeFalse("an unreadable record is left as it is");
    }

    [Fact]
    public void AVerifiedProof_OpensTheDoor_BeyondTheFreeBatchesToo_AndItsStandDownNoteTravels()
    {
        var allowed = Allowed(Auto(batches: 9) with { Proof = new ConsultProof.Verified("abc", "stood down: no consultant answered") });

        allowed.Note.Should().Contain("stood down");
        allowed.Counted.Should().BeTrue();
    }

    [Fact]
    public void NoConsultantCanBeHad_StandsDown()
    {
        var allowed = Allowed(Auto() with { Unavailable = "switched off" });

        allowed.Note.Should().Contain("stood down").And.Contain("switched off");
        allowed.Counted.Should().BeTrue("the person is asked in the building phase, so the batch is recorded");
        allowed.ConsultBeside.Should().BeFalse("there is nobody to run beside it");
    }

    [Theory]
    [InlineData(QuestionMode.Require, AskPhase.Planning, false)]
    [InlineData(QuestionMode.Require, AskPhase.Released, false)]
    [InlineData(QuestionMode.Off, AskPhase.Building, false)]
    [InlineData(QuestionMode.Require, AskPhase.Building, true)]
    public void TheStandDown_IsCountedOnlyWhereTheBatchesAreKept(QuestionMode mode, AskPhase phase, bool phaseUnreadable)
    {
        var allowed = Allowed(Auto(mode, phase) with { Unavailable = "no row on", PhaseUnreadable = phaseUnreadable });

        allowed.Note.Should().Contain("no row on");
        allowed.Counted.Should().BeFalse();
    }

    [Fact]
    public void AVerifiedProof_BeatsAnUnavailableConsultant()
    {
        var allowed = Allowed(Auto() with { Proof = new ConsultProof.Verified("abc", "the consultants answered"), Unavailable = "switched off" });

        allowed.Note.Should().Be("the consultants answered", "the consultation happened; the stand-down is for a question that was never consulted");
    }

    [Theory]
    [InlineData(QuestionMode.Require, AskPhase.Building, 0, "")]
    [InlineData(QuestionMode.Require, AskPhase.Building, 2, "")]
    [InlineData(QuestionMode.Require, AskPhase.Building, 2, "switched off")]
    [InlineData(QuestionMode.Remind, AskPhase.Building, 5, "")]
    [InlineData(QuestionMode.Off, AskPhase.Building, 0, "")]
    [InlineData(QuestionMode.Require, AskPhase.Planning, 0, "")]
    [InlineData(QuestionMode.Require, AskPhase.Released, 0, "")]
    public void AProductionRisk_IsTodaysDecision(QuestionMode mode, AskPhase phase, int batches, string unavailable)
    {
        foreach (var unreadable in new[] { false, true })
        {
            foreach (var proof in new ConsultProof[] { ConsultProof.Nothing, new ConsultProof.Rejected("abc", "too old"), new ConsultProof.Verified("abc", "") })
            {
                var input = Auto(mode, phase, batches) with
                {
                    ProductionRisk = true,
                    RiskReason = Reason,
                    Unavailable = unavailable,
                    PhaseUnreadable = unreadable,
                    Proof = proof,
                };

                AskGate.Decide(input).Should().Be(AskGate.Decide(input with { Autonomous = false }),
                    "O3: a declared production risk keeps its consultants-first path under auto");
            }
        }
    }

    [Fact]
    public void AProductionRisk_InTheBuildingPhase_StillRunsItsConsultants()
    {
        var allowed = Allowed(Auto() with { ProductionRisk = true, RiskReason = Reason });

        allowed.ConsultBeside.Should().BeTrue("the shipped risk path is the consultants first, their answers in the reply (G3)");
        allowed.Counted.Should().BeTrue();
    }

    [Theory]
    [InlineData(AskDoor.Conversation, "")]
    [InlineData(AskDoor.Conversation, "  ")]
    [InlineData(AskDoor.Card, "")]
    [InlineData(AskDoor.Card, "  ")]
    public void ARiskWithNoReason_IsStillRefused(AskDoor door, string reason)
    {
        var refused = AskGate.Decide(Auto() with { ProductionRisk = true, RiskReason = reason, Door = door })
            .Should().BeOfType<AskDecision.Refused>().Subject;

        refused.Sentence.Should().Contain("riskReason", "the refusal comes before the policy, in every mode and on both doors");
    }

    [Theory]
    [InlineData(false, QuestionMode.Require, AskPhase.Building)]
    [InlineData(false, QuestionMode.Off, AskPhase.Planning)]
    [InlineData(true, QuestionMode.Require, AskPhase.Building)]
    [InlineData(true, QuestionMode.Remind, AskPhase.Released)]
    public void TheCardDoor_UnderAuto_RunsNoConsultant(bool risk, QuestionMode mode, AskPhase phase)
    {
        var input = Auto(mode, phase, batches: 9) with
        {
            Door = AskDoor.Card,
            ProductionRisk = risk,
            RiskReason = risk ? Reason : string.Empty,
            Unavailable = "switched off",
            PhaseUnreadable = true,
            Proof = new ConsultProof.Rejected("abc", "too old"),
        };

        var allowed = Allowed(input);

        allowed.ConsultBeside.Should().BeFalse("no consultant runs beside a card that is never written");
        allowed.Counted.Should().BeFalse("no card is written, so no batch is");
        allowed.Note.Should().BeEmpty("the service answers the card door at once, in its own words");
    }

    [Fact]
    public void TheCardDoor_WithAVerifiedProofAndARisk_RunsNoConsultantEither()
    {
        var allowed = Allowed(Auto() with
        {
            Door = AskDoor.Card,
            ProductionRisk = true,
            RiskReason = Reason,
            Proof = new ConsultProof.Verified("abc", "the consultants answered"),
        });

        allowed.ConsultBeside.Should().BeFalse();
        allowed.Counted.Should().BeFalse();
        allowed.Note.Should().BeEmpty();
    }

    [Fact]
    public void WithAutonomousFalse_TheInputIsTheShippedGate()
    {
        new AskGateInput().Autonomous.Should().BeFalse("every row of AskGateTests is unchanged");
        AskGate.Decide(Auto() with { Autonomous = false }).Should().BeOfType<AskDecision.Allowed>("the first free batch, as before");
    }
}
