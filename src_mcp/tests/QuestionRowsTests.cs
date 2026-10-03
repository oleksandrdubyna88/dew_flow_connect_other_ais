using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>COAI_QCONSULT_ROWS</c> as the server reads it (PLAN_question_consultant.md, S2): a model and exactly one
/// prompt per row, the same prompt allowed on several rows, at most six on.
/// </summary>
public sealed class QuestionRowsTests
{
    private const string ThreeRows = """
        [
          {"id":"sonnet-disk","vendor":"claude","runtime":"claude","model":"sonnet","prompt":"question-disk"},
          {"id":"astra-web","vendor":"codex","runtime":"codex","model":"gpt-6-astra","prompt":"question-web","acknowledged":true},
          {"id":"grok","vendor":"grok-openrouter","runtime":"api","baseUrl":"https://openrouter.ai/api/v1","model":"x-ai/grok-4.7","key":"openrouter","prompt":"question-opinion","enabled":false}
        ]
        """;

    [Fact]
    public void TheOperatorsThreeRows_ReadBack_FieldForField()
    {
        var setting = QuestionRows.Parse(ThreeRows);

        setting.Unreadable.Should().BeFalse();
        setting.Complaints.Should().BeEmpty();
        setting.Rows.Should().HaveCount(3);
        var astra = setting.Rows.Single(r => r.Id == "astra-web");
        astra.Vendor.Should().Be("codex");
        astra.Runtime.Should().Be("codex");
        astra.Prompt.Should().Be("question-web");
        // The row carries "acknowledged": true — a field the pre-release build wrote. It is read past without a complaint (D13, revised).
        astra.Enabled.Should().BeTrue("absent is on");
        var grok = setting.Rows.Single(r => r.Id == "grok");
        grok.Enabled.Should().BeFalse();
        grok.Key.Should().Be("openrouter", "an api row's vault key name travels with it (S3.6)");
        grok.BaseUrl.Should().Be("https://openrouter.ai/api/v1");
        setting.Active.Select(r => r.Id).Should().Equal("sonnet-disk", "astra-web");
    }

    [Fact]
    public void TheSamePromptOnSeveralRows_IsOrdinary()
    {
        var setting = QuestionRows.Parse("""
            [{"id":"a","vendor":"claude","runtime":"claude","model":"sonnet","prompt":"question-disk"},
             {"id":"b","vendor":"claude","runtime":"claude","model":"opus","prompt":"question-disk"}]
            """);

        setting.Complaints.Should().BeEmpty("two models on one prompt is the operator's own example");
        setting.Active.Should().HaveCount(2);
    }

    [Fact]
    public void TheSeventhActiveRow_IsSwitchedOff_AndSaid()
    {
        var rows = string.Join(",", Enumerable.Range(1, 8).Select(n => $$"""{"id":"r{{n}}","vendor":"claude","runtime":"claude","prompt":"question-opinion"}"""));

        var setting = QuestionRows.Parse("[" + rows + "]");

        setting.Active.Should().HaveCount(QuestionRows.MaxActive, "at most six rows run");
        setting.Active.Select(r => r.Id).Should().Equal(["r1", "r2", "r3", "r4", "r5", "r6"], "the first six by position stay on");
        setting.Rows.Should().HaveCount(8, "the rows are kept, switched off, not dropped");
        setting.Complaints.Should().HaveCount(2);
        setting.Complaints[0].Should().Contain("'r7'").And.Contain("seventh").And.Contain("switched off");
        setting.Complaints[1].Should().Contain("'r8'");
    }

    [Fact]
    public void ADisabledRow_DoesNotCountTowardsTheSix()
    {
        var rows = string.Join(",", Enumerable.Range(1, 7).Select(n => $$"""{"id":"r{{n}}","vendor":"claude","runtime":"claude","prompt":"question-opinion","enabled":{{(n == 1 ? "false" : "true")}}}"""));

        var setting = QuestionRows.Parse("[" + rows + "]");

        setting.Active.Should().HaveCount(6);
        setting.Complaints.Should().BeEmpty("six on and one off is within the cap");
    }

    [Fact]
    public void ARowWithoutAnId_AVendor_OrAPrompt_IsDropped_AndSaid()
    {
        var setting = QuestionRows.Parse("""
            [{"vendor":"claude","prompt":"question-disk"},
             {"id":"no-vendor","prompt":"question-disk"},
             {"id":"no-prompt","vendor":"claude"},
             {"id":"fine","vendor":"claude","prompt":"question-disk"}]
            """);

        setting.Rows.Select(r => r.Id).Should().Equal("fine");
        setting.Complaints.Should().HaveCount(3);
        setting.Complaints[0].Should().Contain("the id ''");
        setting.Complaints[1].Should().Contain("'no-vendor'").And.Contain("no vendor");
        setting.Complaints[2].Should().Contain("'no-prompt'").And.Contain("no prompt");
    }

    [Fact]
    public void ASecondRowUnderOneId_IsDropped_AndSaid()
    {
        var setting = QuestionRows.Parse("""
            [{"id":"dup","vendor":"claude","prompt":"question-disk"},
             {"id":"DUP","vendor":"codex","prompt":"question-web"}]
            """);

        setting.Rows.Should().ContainSingle().Which.Vendor.Should().Be("claude", "the first row keeps the id");
        setting.Complaints.Should().ContainSingle().Which.Should().Contain("'DUP'").And.Contain("twice");
    }

    [Fact]
    public void AnEmptyOrAbsentSetting_IsNoRows_AndNoComplaint()
    {
        foreach (var value in (string?[])[null, "", "  ", "[]"])
        {
            var setting = QuestionRows.Parse(value);

            setting.Rows.Should().BeEmpty();
            setting.Complaints.Should().BeEmpty();
            setting.Unreadable.Should().BeFalse();
        }
    }

    [Fact]
    public void AValueThatIsNotJson_IsUnreadable_NamingTheKey()
    {
        var setting = QuestionRows.Parse("[{\"id\": \"half");

        setting.Unreadable.Should().BeTrue("the tool refuses by name rather than running nobody");
        setting.Rows.Should().BeEmpty();
        setting.Complaints.Should().ContainSingle().Which.Should().Contain("COAI_QCONSULT_ROWS could not be read");
    }

    [Fact]
    public void AnOmittedField_ReadsAsEmpty_NeverNull()
    {
        var row = QuestionRows.Parse("""[{"id":"bare","vendor":"codex","prompt":"question-web"}]""").Rows.Single();

        row.Runtime.Should().BeEmpty();
        row.Model.Should().BeEmpty();
        row.BaseUrl.Should().BeEmpty();
        row.ExecutablePath.Should().BeEmpty();
        row.Key.Should().BeEmpty();
    }
}
