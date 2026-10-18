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
public sealed class OneBadFieldCostsItsRowOnlyTests
{
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
}
