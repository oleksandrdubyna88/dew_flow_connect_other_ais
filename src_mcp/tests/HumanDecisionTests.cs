using CoaiMcp.Core.Findings;
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

    [Fact]
    public void ADecisionToCarryOn_IsReadableBySession_NotOnlyByAWaitingCall()
    {
        var escalations = Escalations();
        escalations.Notify(Question("q1", "s1"));
        File.WriteAllText(escalations.AnswerPath("q1"), """{"id":"q1","answer":"keep going","decision":"continue","answeredUtc":"now"}""");

        escalations.DecisionFor("s1").Should().Be(HumanDecision.Continue);
    }

    [Fact]
    public void ADecisionToFixFirst_IsAlsoRecorded_BecauseSilenceAndRefusalAreDifferent()
    {
        var escalations = Escalations();
        escalations.Notify(Question("q2", "s2"));
        File.WriteAllText(escalations.AnswerPath("q2"), """{"id":"q2","answer":"fix them first","decision":"fix","answeredUtc":"now"}""");

        escalations.DecisionFor("s2").Should().Be(HumanDecision.Fix);
    }

    [Fact]
    public void AnUnansweredNotice_IsNotADecision()
    {
        var escalations = Escalations();
        escalations.Notify(Question("q3", "s3"));

        escalations.DecisionFor("s3").Should().Be(HumanDecision.None);
    }

    [Fact]
    public void ANoticeForAnotherSession_IsNotThisSessionsDecision()
    {
        var escalations = Escalations();
        escalations.Notify(Question("q4", "other-session"));
        File.WriteAllText(escalations.AnswerPath("q4"), """{"id":"q4","answer":"keep going","decision":"continue","answeredUtc":"now"}""");

        escalations.DecisionFor("mine").Should().Be(HumanDecision.None);
    }

    [Fact]
    public void FreeTextWithNoDecisionField_StaysAnAnswer_NotAnOverride()
    {
        // An older panel, or somebody typing a sentence: the text is still their answer and must
        // not be lost, but it is not a button press and must never advance a stage by itself.
        var escalations = Escalations();
        escalations.Notify(Question("q5", "s5"));
        File.WriteAllText(escalations.AnswerPath("q5"), """{"id":"q5","answer":"looks fine to me","answeredUtc":"now"}""");

        escalations.DecisionFor("s5").Should().Be(HumanDecision.None);
        escalations.AnswerTextFor("s5").Should().Be("looks fine to me");
    }
}
