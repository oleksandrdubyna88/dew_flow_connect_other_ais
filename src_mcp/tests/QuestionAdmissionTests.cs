using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// D13 on the SERVER (<c>todo/PLAN_question_consultant.md</c>, revised by the operator on 2026-10-03): a pair the runtime
/// can do but cannot be CONFINED on — codex for every capability, agy's disk read, held only by a headless default — is
/// ADMITTED and FLAGGED. The flag ("can read this machine") is what the person sees on the row, the card and the log;
/// there is no acknowledgement to tick, because codex has no setting that would confine what it reads.
/// </summary>
public sealed class QuestionAdmissionTests
{
    private static QuestionRow Row(string vendor, string runtime, string prompt) =>
        new("r1", vendor, runtime, "m", string.Empty, string.Empty, string.Empty, prompt, Enabled: true);

    private static QuestionConsultSettings Settings(params string[] roots) => new() { Roots = roots };

    private static RowAdmission Admit(QuestionRow row, QuestionConsultSettings settings) =>
        QuestionAdmission.Admit(row, settings, [], Core.Api.ApiOverrides.None);

    [Theory]
    [InlineData("question-web")]
    [InlineData("question-disk")]
    [InlineData("question-opinion")]
    public void AnUnconfinedPair_IsAdmitted_WithNothingToTick_AndStaysFlagged(string prompt)
    {
        var admitted = Admit(Row("codex", "codex", prompt), Settings(Path.GetTempPath()))
            .Should().BeOfType<RowAdmission.Admitted>("the flag is enough — nothing the operator ticks would confine codex").Subject;

        admitted.Plan.Flag.Should().Be(AdmissionFlag.Unconfined, "admitted is not confined: the flag travels with every answer");
    }

    [Fact]
    public void ADefaultDenyPair_IsAdmitted_AndFlaggedAsSuch()
    {
        Admit(Row("antigravity", "antigravity", "question-disk"), Settings(Path.GetTempPath()))
            .Should().BeOfType<RowAdmission.Admitted>().Which.Plan.Flag.Should().Be(AdmissionFlag.DefaultDeny);
    }

    [Fact]
    public void AConfinedPair_CarriesNoFlag()
    {
        var admitted = Admit(Row("claude", "claude", "question-opinion"), Settings())
            .Should().BeOfType<RowAdmission.Admitted>().Subject;

        admitted.Plan.Flag.Should().Be(AdmissionFlag.None, "a measured mechanism confines it");
    }

    [Fact]
    public void ABlockedPair_IsStillBlocked_WithItsRealReason()
    {
        // A3's refusal is untouched: a pair the runtime cannot do at all is blocked whatever the row says.
        var refused = Admit(Row("antigravity", "antigravity", "question-web"), Settings())
            .Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Status.Should().Be(RowOutcomes.Blocked);
        refused.Reason.Should().NotContain("acknowledg", "there is no tick any more; the sentence names the real reason");
    }
}
