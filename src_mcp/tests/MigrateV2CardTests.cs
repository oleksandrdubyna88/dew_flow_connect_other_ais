using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The escalation cards half of the v2 migration (E1.S4, scenarios 6 and 7): an OPEN card is withdrawn and archived, a card that is history stays where it is.
/// </summary>
/// <remarks>Cards are the four shapes v1 releases wrote (<see cref="MigrateV2Data"/>): bare, <c>status</c>/<c>expiredUtc</c>, <c>kind</c>, <c>decisions</c>.</remarks>
public sealed class MigrateV2CardTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-migrate-v2-");

    private string Data => _dir.Path;

    public void Dispose() => _dir.Dispose();

    private static JsonObject Card(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    // ---------- scenario 6 ----------

    [Theory]
    [InlineData("f1")]
    [InlineData("f2")]
    [InlineData("f3")]
    public void S6_AnOpenCardWithNoSession_IsWithdrawnInPlace_AndArchived_WithEveryOtherMemberKept(string format)
    {
        MigrateV2Data.Plant(format, "plan-pending", Data, sessions: false);
        var card = Directory.GetFiles(Path.Combine(Data, "escalations"), "*.json").Single();
        var id = Path.GetFileNameWithoutExtension(card);
        var original = Card(card);

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Cards.Should().Be(1);
        File.Exists(card).Should().BeFalse("the card left the folder a person's panel watches");
        var archived = Card(Path.Combine(Data, "escalations", "archive", id + ".json"));
        archived["status"]!.GetValue<string>().Should().Be("withdrawn_v2");
        foreach (var (name, value) in original.Where(m => m.Key != "status"))
        {
            archived[name]?.ToJsonString().Should().Be(value?.ToJsonString(), $"member '{name}' is kept");
        }

        archived.Select(m => m.Key).Should().BeEquivalentTo(original.Select(m => m.Key).Append("status").Distinct(), "no member is dropped or invented");
    }

    [Theory]
    [InlineData("f1")]
    [InlineData("f2")]
    [InlineData("f3")]
    public void S6_AHeldSession_WhoseCardIsMissing_StillConverts(string format)
    {
        MigrateV2Data.Plant(format, "plan-pending", Data, cards: false);
        var file = MigrateV2Data.SessionFile(Data, "plan");

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Should().BeEquivalentTo(new MigrateV2Counts(1, 0, 0, 0, 0, 0, 0));
        LegacyHold.Stands(MigrateV2Data.Load(Data, file).State).Should().BeFalse("a session is converted by what ITS file says, never by a card it names");
    }

    // ---------- scenario 7 ----------

    [Fact]
    public void S7_AnAnsweredCard_AndAnExpiredOne_AreHistory_AndStayExactlyWhereTheyAre()
    {
        MigrateV2Data.Plant("f3", "plan-answered", Data, sessions: false);
        MigrateV2Data.Plant("f3", "plan-auto-released", Data, sessions: false);
        var before = MigrateV2Data.Tree(Data);
        Directory.GetFiles(Path.Combine(Data, "escalations"), "*.json").Should().HaveCount(3, "an answered card, its answer, and an expired card");

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Cards.Should().Be(0);
        MigrateV2Data.Tree(Data).Should().Equal(before, "no card or answer changed");
        Directory.Exists(Path.Combine(Data, "escalations", "archive")).Should().BeFalse("nothing was archived");
    }

    [Theory]
    [InlineData("f2")]
    [InlineData("f3")]
    public void S7_AHeldSession_WithAnAnsweredCard_Converts_AndIsListedInAnsweredHolds(string format)
    {
        MigrateV2Data.Plant(format, "plan-pending", Data);
        var open = Directory.GetFiles(Path.Combine(Data, "escalations"), "*.json").Single();
        var id = Path.GetFileNameWithoutExtension(open);
        File.WriteAllText(
            Path.Combine(Data, "escalations", id + ".answer.json"),
            """{"id":"x","answer":"Fix the findings","decision":"fix","answeredUtc":"2026-11-20T10:00:00Z"}""");
        var file = MigrateV2Data.SessionFile(Data, "plan");
        var answered = MigrateV2Data.Tree(Data).Where(e => e.Key.StartsWith("escalations/", StringComparison.Ordinal)).ToList();

        var report = MigrateV2Data.Apply(Data);

        LegacyHold.Stands(MigrateV2Data.Load(Data, file).State).Should().BeFalse("v2 never waits: a person's answer v1 had not applied is overridden by good_enough");
        report.Counts.Sessions.Should().Be(1);
        report.Counts.Cards.Should().Be(0, "the card has an answer: it is history, not an open card");
        report.AnsweredHolds.Should().ContainSingle().Which.Should().Be(new MigrateV2AnsweredHold(Path.GetFileName(file), id, "fix"));
        MigrateV2Data.Tree(Data).Where(e => e.Key.StartsWith("escalations/", StringComparison.Ordinal)).Should().Equal(answered);
    }

    [Fact]
    public void S7_ACardWithAnEmptyOrMalformedAnswerFile_IsStillOpen_AndIsWithdrawn()
    {
        MigrateV2Data.Plant("f3", "plan-pending", Data, sessions: false);
        var card = Directory.GetFiles(Path.Combine(Data, "escalations"), "*.json").Single();
        var id = Path.GetFileNameWithoutExtension(card);
        File.WriteAllText(Path.Combine(Data, "escalations", id + ".answer.json"), """{"id":"x","answer":""}""");

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Cards.Should().Be(1, "an empty answer is not an answer: a half-written file must not keep a card open for ever");
        File.Exists(Path.Combine(Data, "escalations", "archive", id + ".json")).Should().BeTrue();
    }
}
