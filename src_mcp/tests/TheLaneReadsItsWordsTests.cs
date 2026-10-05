using CoaiMcp.Core.Context;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>COAI_SECURITY_LANE</c> carries a signal's words (<c>signals</c>) and a card's own words (a prompt's <c>words</c>),
/// and the lane detects with them (todo/PLAN_one_model_catalog.md, epic 2, story 4).
/// </summary>
/// <remarks>
/// Absent, the shipped words. A pattern the engine refuses is a COMPLAINT naming the pattern and its signal — the lane
/// still runs on everything else — and so is a signal this build does not know. Neither is a refused lane.
/// </remarks>
public sealed class TheLaneReadsItsWordsTests
{
    private static readonly ProviderSettings[] Rows = [new("qwen") { Runtime = "local" }];

    private static SecurityLaneSetting Lane(string json) => SecurityLaneSetting.Parse(json, Rows);

    private static FileDiff File(string text) => new("src/Pay.cs", text, false);

    [Fact]
    public void WithNoWords_TheLaneDetectsWithTheShippedTable()
    {
        var lane = Lane("""{"enabled":true,"runs":[{"vendor":"qwen","prompt":"redteam-sql"}]}""");

        SecuritySignals.Classify([File("new DbContext();")], lane.Table)[0].Signals.Should().Contain("sql");
        lane.Complaints.Should().BeEmpty();
    }

    [Fact]
    public void ASignalsWords_FromTheSetting_AreWhatTheLaneDetectsWith()
    {
        var lane = Lane("""{"enabled":true,"signals":{"secrets":["vaultread("]},"runs":[{"vendor":"qwen","prompt":"redteam-sql"}]}""");

        SecuritySignals.Classify([File("VaultRead(\"x\");")], lane.Table)[0].Signals.Should().Contain("secrets");
        SecuritySignals.Classify([File("password = 1;")], lane.Table)[0].Signals.Should().NotContain("secrets", "the person's words replaced the shipped ones");
        lane.Complaints.Should().BeEmpty("signals is a member this build reads");
    }

    [Fact]
    public void ACardsOwnWords_MakeItDueOnTheirMatch()
    {
        var lane = Lane("""
            {"enabled":true,"prompts":[{"id":"redteam-billing","words":["acme.charge(","/\\bacme\\d{3}\\b/"]}],
             "runs":[{"vendor":"qwen","prompt":"redteam-billing"}]}
            """);
        var card = lane.Prompts.Single(p => p.Id == "redteam-billing");

        card.Words.Should().Equal("acme.charge(", "/\\bacme\\d{3}\\b/");
        card.Refusal.Should().BeEmpty("words is a member this build reads");
        SecuritySignals.Triggered(card, SecuritySignals.Classify([File("Acme.Charge(o);")], lane.Table)).Should().BeTrue();
        SecuritySignals.Triggered(card, SecuritySignals.Classify([File("nothing")], lane.Table)).Should().BeFalse();
    }

    [Fact]
    public void ARefusedPattern_IsAComplaintNamingIt_AndTheLaneStillRuns()
    {
        var lane = Lane("""{"enabled":true,"signals":{"secrets":["/(?=x)/","password"]},"runs":[{"vendor":"qwen","prompt":"redteam-sql"}]}""");

        lane.Enabled.Should().BeTrue();
        lane.Complaints.Should().ContainSingle().Which.Should().Contain("/(?=x)/").And.Contain("secrets").And.Contain("lookaround");
        SecuritySignals.Classify([File("password = 1;")], lane.Table)[0].Signals.Should().Contain("secrets");
    }

    [Fact]
    public void AnUnknownSignal_IsAComplaint_NotARefusedLane()
    {
        var lane = Lane("""{"enabled":true,"signals":{"teleport":["beam"]},"runs":[{"vendor":"qwen","prompt":"redteam-sql"}]}""");

        lane.Enabled.Should().BeTrue();
        lane.Complaints.Should().ContainSingle().Which.Should().Contain("teleport");
    }
}
