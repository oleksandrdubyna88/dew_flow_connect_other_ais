using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultant carries its WHOLE catalog row — key name, price, api settings, system prompt, timeout, CLI effort — not
/// only the five launch fields (the cadence consultation for epics 1–3 of todo/PLAN_one_model_catalog.md, finding C2).
/// </summary>
/// <remarks>
/// The extension writes the row, exactly as it writes a reviewer's, beside the entry's <c>vendor</c> as <c>row</c> — only to
/// a binary that lists <c>consultantRow</c>. The server reads it with the reviewer row's own parser; identity (the id a
/// consultation resumes by, the runtime, the remote allowlist) stays the entry's.
/// </remarks>
public sealed class AConsultantCarriesItsWholeRowTests
{
    private static PanelSettings From(string consultants) =>
        PanelSettings.FromEnvironment(name => name == "COAI_CONSULTANTS" ? consultants : null);

    private static ResolvedConsultant Resolve(string consultants)
    {
        var settings = From(consultants);

        return ConsultantResolver.Resolve(settings.Consultants[CallerIdentity.Claude], CallerIdentity.Claude, settings.Providers);
    }

    private const string ApiRow =
        """{"claude":{"vendor":"glm","runtime":"api","model":"glm-5.3","baseUrl":"https://glm.example/v4","executablePath":"","row":{"id":"glm-2","runtime":"api","model":"glm-5.3","baseUrl":"https://glm.example/v4","dialect":"glm","key":"glm","effort":"high","thinking":false,"systemPrompt":"Answer in plain English.","timeoutMinutes":7,"price":{"in":2,"out":6}}}}""";

    [Fact]
    public void AnApiRowOnTheWire_ReachesTheConsultantWithEveryOption()
    {
        var resolved = Resolve(ApiRow);

        var row = resolved.Should().BeOfType<ResolvedConsultant.Definition>().Subject.Vendor;
        row.Provider.Should().Be("glm", "the id a consultation resumes by stays the entry's");
        row.Runtime.Should().Be("api");
        row.Dialect.Should().Be("glm");
        row.VaultKey.Should().Be("glm");
        row.Api.Effort.Should().Be("high");
        row.Api.Thinking.Should().Be(Core.Api.ThinkingSetting.Off);
        row.SystemPrompt.Should().Be("Answer in plain English.");
        row.TimeoutMinutes.Should().Be(7);
        row.Price.Should().NotBe(Core.Findings.TokenPrice.None);
    }

    [Fact]
    public void ACliRowOnTheWire_ReachesTheConsultantWithItsEffort()
    {
        var resolved = Resolve(
            """{"claude":{"vendor":"codex","runtime":"codex","model":"gpt-6","baseUrl":"","executablePath":"","row":{"id":"codex","runtime":"codex","model":"gpt-6","effort":"low","systemPrompt":"Be brief."}}}""");

        var row = resolved.Should().BeOfType<ResolvedConsultant.Definition>().Subject.Vendor;
        row.CliEffort.Should().Be("low");
        row.SystemPrompt.Should().Be("Be brief.");
    }

    [Fact]
    public void TheRowNeverChangesWhoTheConsultantIs()
    {
        // A row naming another runtime or id is the row's word against the entry's: the entry decides who is launched.
        var resolved = Resolve(
            """{"claude":{"vendor":"codex","runtime":"codex","model":"gpt-6","row":{"id":"elsewhere","runtime":"claude","model":"opus","baseUrl":"https://evil.example","executablePath":"/tmp/x"}}}""");

        var row = resolved.Should().BeOfType<ResolvedConsultant.Definition>().Subject.Vendor;
        row.Provider.Should().Be("codex");
        row.Runtime.Should().Be("codex");
        row.Model.Should().Be("gpt-6");
        row.BaseUrl.Should().BeEmpty();
        row.ExecutablePath.Should().BeEmpty();
    }

    [Fact]
    public void ARowThatDoesNotParse_RefusesTheConsultantByName()
    {
        var resolved = Resolve("""{"claude":{"vendor":"codex","runtime":"codex","model":"gpt-6","row":{"id":"codex","timeoutMinutes":"seven"}}}""");

        resolved.Should().BeOfType<ResolvedConsultant.Unavailable>().Which.Why.Should().Contain("codex");
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"a string\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ARowThatIsNotARow_RefusesTheConsultantByName_NeverThrows(string row)
    {
        // The own review of epic 4's code round: only a JsonException was caught, so a row of another JSON KIND could
        // have failed the whole turn instead of refusing this consultant.
        var resolved = Resolve("""{"claude":{"vendor":"codex","runtime":"codex","model":"gpt-6","row":""" + row + "}}");

        resolved.Should().BeOfType<ResolvedConsultant.Unavailable>().Which.Why.Should().Contain("could not be read");
    }

    [Fact]
    public void ANullRow_IsNoRow()
    {
        Resolve("""{"claude":{"vendor":"codex","runtime":"codex","model":"gpt-6","row":null}}""")
            .Should().BeOfType<ResolvedConsultant.Definition>();
    }

    [Fact]
    public void AnEntryWithoutARow_IsTheFiveFieldsAsAlways()
    {
        var resolved = Resolve("""{"claude":{"vendor":"codex","runtime":"codex","model":"gpt-6","baseUrl":"","executablePath":""}}""");

        var row = resolved.Should().BeOfType<ResolvedConsultant.Definition>().Subject.Vendor;
        row.SystemPrompt.Should().BeEmpty();
        row.CliEffort.Should().BeEmpty();
        row.TimeoutMinutes.Should().Be(0);
    }

    [Fact]
    public void AQuestionRowOnTheWire_ReachesItsLaunchWithEveryOption()
    {
        var rows = Core.QuestionConsult.QuestionRows.Parse(
            """[{"id":"q-high","vendor":"glm","runtime":"api","model":"glm-5.3","baseUrl":"https://glm.example/v4","executablePath":"","key":"","prompt":"code","enabled":true,"row":{"id":"glm-high","runtime":"api","model":"glm-5.3","baseUrl":"https://glm.example/v4","dialect":"glm","key":"glm","effort":"high","systemPrompt":"Be brief."}}]""");

        var row = QuestionRowResolver.Resolve(rows.Rows[0], []).Should().BeOfType<ResolvedQuestionRow.Vendor>().Subject.Provider;

        row.Provider.Should().Be("glm");
        row.Dialect.Should().Be("glm");
        row.VaultKey.Should().Be("glm");
        row.Api.Effort.Should().Be("high", "two catalog rows on one key are told apart by what they carry");
        row.SystemPrompt.Should().Be("Be brief.");
    }

    [Fact]
    public void AQuestionRowWhoseRowDoesNotParse_IsUnavailableByName()
    {
        var rows = Core.QuestionConsult.QuestionRows.Parse(
            """[{"id":"q-1","vendor":"codex","runtime":"codex","model":"gpt-6","prompt":"code","enabled":true,"row":{"id":"codex","timeoutMinutes":"seven"}}]""");

        QuestionRowResolver.Resolve(rows.Rows[0], []).Should().BeOfType<ResolvedQuestionRow.Unavailable>().Which.Why.Should().Contain("q-1");
    }

    [Fact]
    public void TheBinaryListsConsultantRow()
    {
        FeaturesMode.Listed.Should().Contain("consultantRow");
    }
}
