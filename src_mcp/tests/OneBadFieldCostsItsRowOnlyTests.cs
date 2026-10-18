using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One bad field in one reviewer row costs THAT row only (todo/PLAN_one_bad_field_keeps_the_rows.md).
/// </summary>
/// <remarks>
/// Before this, a member of the wrong JSON type (<c>"timeoutMinutes":"30"</c>) made the whole `COAI_VENDORS` list
/// unreadable: zero rows, and the caller then ran the shipped pair — reviewers the person never chose — with nothing said.
/// A null row did worse: it threw out of <c>FromEnvironment</c>.
/// </remarks>
public sealed class OneBadFieldCostsItsRowOnlyTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("coai-bad-field-");

    public void Dispose() => _data.Dispose();

    private const string Gemini = """{"id":"gemini","runtime":"gemini"}""";

    private static PanelSettings From(string vendors) =>
        PanelSettings.FromEnvironment(name => name == "COAI_VENDORS" ? vendors : null);

    [Theory]
    [InlineData("\"timeoutMinutes\":\"30\"")]
    [InlineData("\"timeoutMinutes\":30.5")]
    [InlineData("\"timeoutMinutes\":30.0")]
    [InlineData("\"timeoutMinutes\":99999999999")]
    [InlineData("\"plan\":\"false\"")]
    [InlineData("\"model\":5")]
    [InlineData("\"price\":{\"in\":\"3\"}")]
    [InlineData("\"stream\":1")]
    [InlineData("\"document\":\"no\"")]
    public void AWrongTypeInOneRow_CostsOnlyThatRow(string badMember)
    {
        var rows = PanelSettings.ParseVendors($$"""[{"id":"claude","runtime":"claude",{{badMember}}},{{Gemini}}]""");

        rows.Select(row => row.Provider).Should().Equal("gemini");
    }

    [Fact]
    public void ANullRow_IsLeftOut_AndTheSettingsStillRead()
    {
        var read = () => From($"[null,{Gemini}]");

        read.Should().NotThrow().Which.Providers.Select(p => p.Provider).Should().Equal("gemini");
    }

    [Fact]
    public void ACleanList_ReadsEveryRow_AndSaysNothing()
    {
        var settings = From($$"""[{"id":"claude","runtime":"claude","timeoutMinutes":30,"plan":false},{{Gemini}}]""");

        settings.Providers.Select(p => p.Provider).Should().Equal("claude", "gemini");
        settings.Providers[0].TimeoutMinutes.Should().Be(30);
        settings.Providers[0].Plan.Should().BeFalse();
        settings.Unrecognised.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "one of its members")]
    [InlineData("", "one of its members")]
    [InlineData("$", "one of its members")]
    [InlineData("$.timeoutMinutes", "timeoutMinutes")]
    [InlineData("$.price.in", "price.in")]
    [InlineData("$['odd name']", "['odd name']")]
    public void AMemberPathIsSaidPlainly(string? path, string expected)
    {
        VendorRowReader.MemberOf(path).Should().Be(expected);
    }

    private const string BadClaude = """{"id":"claude","runtime":"claude","timeoutMinutes":"30"}""";

    [Fact]
    public void AWrongTypeInOneRow_IsSaid_ByRowAndMember()
    {
        var settings = From($"[{BadClaude},{Gemini}]");

        settings.Providers.Select(p => p.Provider).Should().Equal("gemini");
        settings.UnrecognisedSettings.Should().ContainSingle()
            .Which.Should().Match<UnrecognisedSetting>(one =>
                one.Key == "COAI_VENDORS[claude]"
                && one.Kind.Length == 0
                && one.Sentence.Contains("\"claude\" (row 1 of 2)", StringComparison.Ordinal)
                && one.Sentence.Contains("timeoutMinutes", StringComparison.Ordinal)
                && one.Sentence.Contains("The other rows still review", StringComparison.Ordinal));
        settings.SettingProblems.Should().BeEmpty("these sentences carry no kind, so they are prose in `unrecognised`");
    }

    [Fact]
    public void ANullRow_IsSaid_ByItsPlace()
    {
        var settings = From($"[null,{Gemini}]");

        settings.UnrecognisedSettings.Should().ContainSingle()
            .Which.Should().Match<UnrecognisedSetting>(one =>
                one.Key == "COAI_VENDORS[row 1 of 2]" && one.Sentence.Contains("row 1 of 2 is left out", StringComparison.Ordinal)
                && one.Sentence.Contains("it is null, not a row", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[null]", "it is null, not a row")]
    [InlineData("[5]", "it is a number, not a row")]
    [InlineData("""[{"id":5}]""", "its member id has a value of the wrong JSON type")]
    [InlineData("""[{"runtime":"claude","timeoutMinutes":"30"}]""", "its member timeoutMinutes has a value of the wrong JSON type")]
    public void ARefusedRowWithNoId_StillRunsNoReviewerThePersonDidNotChoose(string vendors, string why)
    {
        var settings = From(vendors);

        settings.Providers.Should().BeEmpty("a row the person wrote must never be replaced by codex and antigravity");
        settings.UnrecognisedSettings.Should().ContainSingle()
            .Which.Should().Match<UnrecognisedSetting>(one =>
                one.Key == "COAI_VENDORS[row 1 of 1]" && one.Sentence.Contains(why, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRowLeftOut_RunsNoReviewerThePersonDidNotChoose()
    {
        var settings = From($"[{BadClaude}]");

        settings.Providers.Should().BeEmpty();
        settings.UnrecognisedSettings.Should().ContainSingle(one => one.Sentence.Contains("timeoutMinutes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[{oops")]
    [InlineData("{}")]
    public void AListThatIsNotJson_StillFallsBack_AndIsNowSaid(string vendors)
    {
        var settings = From(vendors);

        settings.Providers.Select(p => p.Provider).Should().Equal("codex", "antigravity");
        settings.UnrecognisedSettings.Should().ContainSingle()
            .Which.Should().Match<UnrecognisedSetting>(one =>
                one.Key == "COAI_VENDORS" && one.Sentence.Contains("not a JSON list this server can read", StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenListSentence_CarriesTheParsersPosition() =>
        From("[{oops").UnrecognisedSettings.Single().Sentence.Should().Contain("line 1, character");

    [Fact]
    public void TwoBadRows_AreTwoNotices()
    {
        var written = new List<ServerNotice>();
        var log = Serilog.Core.Logger.None;
        var settings = From($$"""[{{BadClaude}},{"runtime":"codex","stream":1},{{Gemini}}]""");

        StartupNotices.Record(settings, [], new Noticing(notice => { written.Add(notice); return true; }, log), log);

        written.Select(notice => notice.Subject).Should().Equal("COAI_VENDORS[claude]", "COAI_VENDORS[row 2 of 3]");
        written[0].Title.Should().StartWith("COAI_VENDORS: the row \"claude\" (row 1 of 3)");
        written[1].Title.Should().StartWith("COAI_VENDORS: row 2 of 3 is left out");
    }

    [Fact]
    public async Task ProvidersCarriesTheRowAndTheMember()
    {
        var service = new PanelService(
            From($"[{BadClaude},{Gemini}]") with { DataDir = _data },
            VaultKeys.None("no vault"), default, new RecordingLauncher(stdOut: string.Empty), Serilog.Core.Logger.None, Noticing.None);

        var answer = JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        answer.GetProperty("unrecognised").EnumerateArray().Select(one => one.GetString()).Should().ContainSingle()
            .Which.Should().Contain("\"claude\"").And.Contain("timeoutMinutes");
    }

    [Fact]
    public void ACleanList_ReadsThroughTheProvidersAnswerWithNoSentence()
    {
        From($"[{Gemini}]").UnrecognisedSettings.Should().BeEmpty();
    }

    [Fact]
    public void ABadPromptCap_IsStillSilent_AndKeepsItsRow()
    {
        var settings = From("""[{"id":"codex","runtime":"codex","maxPromptKib":"180"},{"id":"gemini","runtime":"gemini"}]""");

        settings.Providers.Select(p => p.Provider).Should().Equal("codex", "gemini");
        settings.UnrecognisedSettings.Should().BeEmpty("maxPromptKib is read as a value; a bad one is no cap, and nothing is said (Q1: no change)");
    }

    [Fact]
    public void ARowsWithNoUsableId_AreStillSkippedWithNoSentence()
    {
        var settings = From("""[{},{"id":"  "}]""");

        settings.Providers.Select(p => p.Provider).Should().Equal("codex", "antigravity");
        settings.UnrecognisedSettings.Should().BeEmpty();
    }

    [Fact]
    public void AConsultantRowWithAWrongType_IsStillRefusedByName()
    {
        var bad = new ConsultantChoice("claude", Runtime: "claude", Row: """{"id":"claude","runtime":"claude","timeoutMinutes":"30"}""");
        var good = new ConsultantChoice("claude", Runtime: "claude", Row: """{"id":"claude","runtime":"claude","timeoutMinutes":30}""");

        ConsultantResolver.OptionsOf(bad).Should().BeNull("a row that does not read is refused by name, never a quiet fall back");
        ConsultantResolver.OptionsOf(good)!.TimeoutMinutes.Should().Be(30);
    }

    [Fact]
    public void ARowOutOfRangeOfTheRightType_IsStillUnsetAndNotSaid()
    {
        var settings = From("""[{"id":"claude","runtime":"claude","timeoutMinutes":0},{"id":"codex","runtime":"codex","timeoutMinutes":1441}]""");

        settings.Providers.Select(p => p.TimeoutMinutes).Should().Equal(0, 0);
        settings.UnrecognisedSettings.Should().BeEmpty();
    }
}
