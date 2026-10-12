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
    public void ADiskRowWhoseEveryRootIsTheOtherSides_IsInactiveOnThisSide_SaidPlainly()
    {
        // Every folder was written from the other side (a WSL window's /home/..., read by the Windows server): on this
        // side the row has nothing to read. It is not a fault here, so it is not BLOCKED — it is not asked, and says why.
        var settings = new QuestionConsultSettings { OtherSideRoots = ["/home/jinx/git"] };

        var refused = Admit(Row("claude", "claude", "question-disk"), settings).Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Status.Should().Be(RowOutcomes.Disabled, "inactive on this side, never a stopped row");
        refused.Reason.Should().Contain("inactive on this side").And.Contain("/home/jinx/git").And.Contain("add a folder of this machine");
    }

    [Fact]
    public void ADiskRowWithNoRootAtAll_IsStillBlocked_AskingForAFolder()
    {
        // Nothing set anywhere is the old case, and keeps its sentence.
        var refused = Admit(Row("claude", "claude", "question-disk"), Settings()).Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Status.Should().Be(RowOutcomes.Blocked);
        refused.Reason.Should().Contain("needs at least one root");
    }

    [Fact]
    public void ARowThatReadsNoDisk_IsAskedWhateverTheRootsAre()
    {
        Admit(Row("claude", "claude", "question-opinion"), new QuestionConsultSettings { OtherSideRoots = ["/home/jinx/git"] })
            .Should().BeOfType<RowAdmission.Admitted>("the roots are a disk prompt's alone");
    }

    [Fact]
    public void AStrandedCatalogRow_IsBlocked_InTheExtensionsOwnWords_AndNeverShowsAnEmptyQuote()
    {
        // research/PLAN_names_and_a_missing_root.md story 2: the extension sends a pick whose catalog row is gone, or no
        // longer ticked, as {vendor:'<id>', runtime:''} and says "<id> is no longer ticked question consultant (or was
        // removed) — pick another model" (qconsultWrite.ts, noModel). The server blocked the same row with a sentence about
        // vendors and runtimes nobody had picked.
        var refused = Admit(Row("consult-claude", string.Empty, "question-opinion"), Settings())
            .Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Status.Should().Be(RowOutcomes.Blocked);
        refused.Reason.Should().Be(
            $"consult-claude is no longer ticked question consultant (or was removed) — pick another model, in {QuestionAdmission.Section}");
        refused.Reason.Should().NotContain("''");
    }

    [Fact]
    public void ARowThatPicksNoModel_SaysPickOne_NeverAnEmptyQuote()
    {
        var refused = Admit(Row(string.Empty, string.Empty, "question-opinion"), Settings())
            .Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Reason.Should().Be($"this row picks no model — pick one, in {QuestionAdmission.Section}");
    }

    [Fact]
    public void ABareRuntimeNameTheQuestionConsultantCannotLaunch_KeepsTheRuntimeListAsItsTail()
    {
        // `gemini` is a runtime this build knows by name, not a catalog row that went away: its cure is a runtime the
        // question consultant launches on, so the list of them stays.
        var refused = Admit(Row("gemini", string.Empty, "question-opinion"), Settings())
            .Should().BeOfType<RowAdmission.Refused>().Subject;

        refused.Reason.Should().Contain(string.Join(", ", Runners.Consultation.QuestionResolution.Answering)).And.NotContain("no longer ticked");
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
