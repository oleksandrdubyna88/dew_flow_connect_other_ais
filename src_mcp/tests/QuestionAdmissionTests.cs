using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// D13 on the SERVER (<c>todo/PLAN_question_consultant.md</c>, decided for S3): a pair the runtime can do but
/// cannot be CONFINED on — codex for every capability, agy's disk read, confined only by a headless default —
/// runs only after the operator ticked the row's acknowledgement. A picker is not an enforcement: the settings
/// file is written by hand as often as by the panel, and a row that reads this machine whatever it is told must
/// not run because somebody typed <c>"enabled": true</c>.
/// </summary>
public sealed class QuestionAdmissionTests
{
    private static QuestionRow Row(string vendor, string runtime, string prompt, bool acknowledged) =>
        new("r1", vendor, runtime, "m", string.Empty, string.Empty, string.Empty, prompt, Enabled: true, Acknowledged: acknowledged);

    private static QuestionConsultSettings Settings(params string[] roots) => new() { Roots = roots };

    private static RowAdmission Admit(QuestionRow row, QuestionConsultSettings settings) =>
        QuestionAdmission.Admit(row, settings, [], Core.Api.ApiOverrides.None);

    [Fact]
    public void AnUnconfinedPair_WithoutTheOperatorsAcknowledgement_IsRefusedNamingTheTick()
    {
        var refused = Admit(Row("codex", "codex", "question-web", acknowledged: false), Settings())
            .Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Status.Should().Be(RowOutcomes.Blocked, "never launched — a RowOutcome, so the reply and the record say why");
        refused.Reason.Should().Contain("acknowledg", "the cure is the tick, and the sentence has to name it")
            .And.Contain("can read this machine", "and why the tick exists — the measured fact D13 rests on");
    }

    [Fact]
    public void AnUnconfinedPair_Acknowledged_IsAdmitted_AndStillFlagged()
    {
        var admitted = Admit(Row("codex", "codex", "question-web", acknowledged: true), Settings())
            .Should().BeOfType<RowAdmission.Admitted>().Subject;

        admitted.Plan.Flag.Should().Be(AdmissionFlag.Unconfined, "the acknowledgement admits the row; it does not make the runtime confined");
    }

    [Fact]
    public void ADefaultDenyPair_NeedsTheAcknowledgementToo()
    {
        var root = Path.GetTempPath();

        Admit(Row("antigravity", "antigravity", "question-disk", acknowledged: false), Settings(root))
            .Should().BeOfType<RowAdmission.Refused>().Which.Reason.Should().Contain("acknowledg");
        Admit(Row("antigravity", "antigravity", "question-disk", acknowledged: true), Settings(root))
            .Should().BeOfType<RowAdmission.Admitted>().Which.Plan.Flag.Should().Be(AdmissionFlag.DefaultDeny);
    }

    [Fact]
    public void AConfinedPair_NeedsNoAcknowledgement()
    {
        var admitted = Admit(Row("claude", "claude", "question-opinion", acknowledged: false), Settings())
            .Should().BeOfType<RowAdmission.Admitted>().Subject;

        admitted.Plan.Flag.Should().Be(AdmissionFlag.None, "a measured mechanism confines it; there is nothing to acknowledge");
    }

    [Fact]
    public void ABlockedPair_IsBlockedBeforeTheTickIsAsked()
    {
        // A3's refusal outranks D13's: a pair the runtime cannot do at all is blocked whatever the row says.
        var refused = Admit(Row("antigravity", "antigravity", "question-web", acknowledged: false), Settings())
            .Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Status.Should().Be(RowOutcomes.Blocked);
        refused.Reason.Should().NotContain("acknowledg", "the tick would not help; the sentence must name the real reason");
    }
}
