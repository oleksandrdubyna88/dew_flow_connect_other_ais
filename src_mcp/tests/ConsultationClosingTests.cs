using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Who may end a consultation, with what word, and what may never be overwritten.
/// </summary>
/// <remarks>
/// <para>A table, like <see cref="ConsultationRules"/> beside it, and for the same reason: every
/// refusal names its cure, and a rule that lives inside an async service is a rule no test reaches.</para>
/// <para>The distinction the whole thing turns on: <c>lapsed</c> is NOT a verdict. It is the server
/// saying the clock ran out, which is a fact about time rather than about whether the advice was any
/// good — so a person may still record what they found. A verdict somebody actually reached is
/// immutable. (issue #309.)</para>
/// </remarks>
public sealed class ConsultationClosingTests
{
    private static ConsultationRecord Record(string outcome = "", string status = ConsultationStatuses.Open) =>
        new("c1", "caller-a", "claude", "s1", "D:/repo", "main", "sha", "codex", "gpt-6", "codex",
            ConsultationMemories.VendorRemembers, 5, "2026-09-17T09:00:00.0000000Z")
        {
            Status = status,
            Outcome = outcome,
        };

    [Fact]
    public void AnotherCallersConsultationIsRefused()
    {
        ConsultationClosing.Refusal(Record(), "caller-b", ConsultationOutcomes.Solved, byPerson: false)
            .Should().Contain("another caller");
    }

    [Fact]
    public void ThePersonIsNotSubjectToTheCallerCheck()
    {
        // A different door on purpose: the panel drives this through a one-shot CLI mode on the
        // person's own machine against their own data directory, which is a stronger position than
        // another AI's, not a weaker one.
        ConsultationClosing.Refusal(Record(), caller: string.Empty, ConsultationOutcomes.Solved, byPerson: true)
            .Should().BeNull();
    }

    [Fact]
    public void AWordOutsideTheClosedSetIsRefused()
    {
        ConsultationClosing.Refusal(Record(), "caller-a", "probably_fine", byPerson: false)
            .Should().Contain("solved");
    }

    [Fact]
    public void LapsedIsTheServersOwnWordAndNobodyElseMayWriteIt()
    {
        // It means the clock ran out. A caller claiming it would be reporting somebody else's fact.
        ConsultationClosing.Refusal(Record(), "caller-a", ConsultationOutcomes.Lapsed, byPerson: false)
            .Should().Contain("the server");
        ConsultationClosing.Refusal(Record(), string.Empty, ConsultationOutcomes.Lapsed, byPerson: true)
            .Should().Contain("the server");
    }

    [Fact]
    public void RepeatingTheSAMEOutcomeSucceedsAndRewritesNothing()
    {
        // The answer to a response that was lost on the way back: a retry must not become an error,
        // and it must not rewrite the record either.
        ConsultationClosing.Refusal(Record(ConsultationOutcomes.Solved), "caller-a", ConsultationOutcomes.Solved, byPerson: false)
            .Should().BeNull();
        ConsultationClosing.Writes(Record(ConsultationOutcomes.Solved), ConsultationOutcomes.Solved)
            .Should().BeFalse("a repeat is already true of the record");
    }

    [Fact]
    public void ADIFFERENTOutcomeOverAVerdictIsARefusedConflict()
    {
        ConsultationClosing.Refusal(Record(ConsultationOutcomes.Solved), "caller-a", ConsultationOutcomes.NotSolved, byPerson: false)
            .Should().Contain("already");
    }

    [Fact]
    public void AVerdictMayBeRecordedOverLapsed()
    {
        // The issue's own screenshot is in this state: a consultation nobody closed, which the clock
        // will end. `lapsed` records THAT, and it is not a verdict — so the person who knows whether
        // the advice worked may still say so.
        ConsultationClosing.Refusal(Record(ConsultationOutcomes.Lapsed, ConsultationStatuses.Closed), string.Empty, ConsultationOutcomes.Solved, byPerson: true)
            .Should().BeNull();
        ConsultationClosing.Writes(Record(ConsultationOutcomes.Lapsed), ConsultationOutcomes.Solved)
            .Should().BeTrue();
    }

    [Fact]
    public void AnAbsentOutcomeOnAnAlreadyClosedRecordMayBeFilled()
    {
        // Every record written before this field existed is in this state, and a server that refused
        // them would leave the whole existing history unclosable.
        ConsultationClosing.Refusal(Record(string.Empty, ConsultationStatuses.Closed), "caller-a", ConsultationOutcomes.NotSolved, byPerson: false)
            .Should().BeNull();
    }

    [Fact]
    public void AFailedConsultationTakesNoVerdict()
    {
        // It never produced advice, so there is nothing to have a verdict about; `Reason` already
        // says what went wrong.
        ConsultationClosing.Refusal(Record(string.Empty, ConsultationStatuses.Failed), "caller-a", ConsultationOutcomes.Solved, byPerson: false)
            .Should().Contain("failed");
    }

    [Fact]
    public void AConsultationThatRanOutOfTurnsSaysSo()
    {
        // The issue asks for something to be recorded, and an empty field records nothing. But a
        // spent budget is not a verdict either — hence its own word rather than `abandoned`.
        var last = ConsultationClosing.Lapse(Record(status: ConsultationStatuses.Open));

        last.Outcome.Should().Be(ConsultationOutcomes.Lapsed);
    }

    [Fact]
    public void LapsingNeverOVERWRITESAVerdictSomebodyReached()
    {
        // A caller that closed its consultation as `solved` and then let the clock run out must
        // keep its verdict: the sweep runs later and would otherwise erase it.
        var already = ConsultationClosing.Lapse(Record(ConsultationOutcomes.Solved));

        already.Outcome.Should().Be(ConsultationOutcomes.Solved);
    }

    /// <summary>A close while a TURN is running is refused, naming the cure.</summary>
    /// <remarks>
    /// <c>asking</c> means a vendor is being asked right now. A close that landed then would be
    /// overwritten by that turn's own write a moment later — the status visibly flapping from closed
    /// back to open — so it is refused in the same shape <see cref="ConsultationRules"/> already uses
    /// to refuse a follow-up on an <c>asking</c> record. (Operator decision, 2026-09-17.)
    /// </remarks>
    [Fact]
    public void AConsultationWithATurnInFlightIsRefused()
    {
        ConsultationClosing.Refusal(Record(status: ConsultationStatuses.Asking), "caller-a", ConsultationOutcomes.Solved, byPerson: false)
            .Should().Contain("wait for its answer");
        ConsultationClosing.Refusal(Record(status: ConsultationStatuses.Asking), string.Empty, ConsultationOutcomes.Solved, byPerson: true)
            .Should().Contain("wait for its answer", "the person's door is not an override of physics");
    }

}
