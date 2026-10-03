using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A person's answer to a `call_human` verdict has to REACH something.
/// </summary>
/// <remarks>
/// <para>The notice was written by the round and the round then returned, so nothing was polling
/// for its answer: the panel wrote <c>&lt;id&gt;.answer.json</c> and no code on either side ever
/// read it. A person could type a decision, watch the card disappear, and have changed nothing —
/// which is a worse dead end than never being asked, because it looks like it worked.</para>
/// <para>The answer is also a CHOICE, not prose. "Proceed anyway, or fix the findings and review
/// again?" has two answers, and a free-text box for it invites a sentence that no code can act on.
/// So the file carries a decision, and the AI's next <c>status</c> or <c>resolve</c> reads it.</para>
/// <para>This is the one legitimate route to a human override: the PERSON pressed the button. It is
/// not the AI deciding, which the tool contract forbids and this does not touch.</para>
/// </remarks>
public sealed class HumanDecisionTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-decision-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private Escalations Escalations() => new(_data);

    private static EscalationQuestion Question(string id, string session) =>
        new(id, session, "D:/r", "main", "The plan review gate needs your decision", "", "en", "", [], "now");

    /// <summary>The answer file as the panel writes it, read back through the one reader the server has.</summary>
    private static EscalationAnswer Answered(Escalations escalations, string id, string json)
    {
        escalations.Notify(Question(id, "s"));
        File.WriteAllText(escalations.AnswerPath(id), json);

        return escalations.ReadAnswer(id)!;
    }

    // Which session's answer counts — the hold's own questions, never the newest card of the session — is
    // TheAnswerBelongsToTheCurrentHoldTests' subject, and status's is TheStatusReportsOnlyTheGatesAnswerTests'. The
    // session-wide read these tests used to exercise (Escalations.DecisionFor) went with its last reader, 2026-10-03.

    [Fact]
    public void ADecisionToCarryOn_IsReadFromTheFile_NotOnlyByAWaitingCall()
    {
        var answer = Answered(Escalations(), "q1", """{"id":"q1","answer":"keep going","decision":"continue","answeredUtc":"now"}""");

        CoaiMcp.Server.Escalations.DecisionOf(answer).Should().Be(HumanDecision.Continue);
    }

    [Fact]
    public void ADecisionToFixFirst_IsAlsoRecorded_BecauseSilenceAndRefusalAreDifferent()
    {
        var answer = Answered(Escalations(), "q2", """{"id":"q2","answer":"fix them first","decision":"fix","answeredUtc":"now"}""");

        CoaiMcp.Server.Escalations.DecisionOf(answer).Should().Be(HumanDecision.Fix);
    }

    [Fact]
    public void AnUnansweredNotice_IsNotADecision()
    {
        var escalations = Escalations();
        escalations.Notify(Question("q3", "s3"));

        escalations.ReadAnswer("q3").Should().BeNull();
        CoaiMcp.Server.Escalations.DecisionOf(escalations.ReadAnswer("q3")).Should().Be(HumanDecision.None);
    }

    /// <summary>
    /// The whole answer, not only its button: the feature stage's second round asks WHEN the person
    /// answered, because an answer older than the round it would admit was about something else (D23).
    /// </summary>
    [Fact]
    public void TheAnswer_IsReadableWhole_WithWhenItWasGiven()
    {
        var answer = Answered(Escalations(), "q6", """{"id":"q6","answer":"once more","decision":"continue","answeredUtc":"2026-09-26T10:00:00.000Z"}""");

        answer.Decision.Should().Be("continue");
        answer.AnsweredUtc.Should().Be("2026-09-26T10:00:00.000Z");
    }

    [Fact]
    public void FreeTextWithNoDecisionField_StaysAnAnswer_NotAnOverride()
    {
        // An older panel, or somebody typing a sentence: the text is still their answer and must
        // not be lost, but it is not a button press and must never advance a stage by itself.
        var answer = Answered(Escalations(), "q5", """{"id":"q5","answer":"looks fine to me","answeredUtc":"now"}""");

        CoaiMcp.Server.Escalations.DecisionOf(answer).Should().Be(HumanDecision.None);
        answer.Answer.Should().Be("looks fine to me");
    }
}
