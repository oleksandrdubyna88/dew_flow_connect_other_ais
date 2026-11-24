using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The sessions half of the v2 migration (E1.S4, scenarios 1–5, 15 and 20): a session a v1 build HELD leaves its hold, and nothing else about any session changes.
/// </summary>
/// <remarks>
/// Every fixture is a hold the real v1 code raised and froze as data (<see cref="MigrateV2Data"/>), in the three file shapes releases wrote. The resolve that follows
/// goes through <c>PanelService.ResolveAsync</c> — the entry a caller uses — so "advances once" is the machine's own answer and not a field read.
/// </remarks>
public sealed class MigrateV2SessionTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-migrate-v2-");

    private string Data => _dir.Path;

    public void Dispose() => _dir.Dispose();

    public static TheoryData<string, string, bool> EveryHold()
    {
        var rows = new TheoryData<string, string, bool>();
        foreach (var format in MigrateV2Data.Formats)
        {
            foreach (var kind in MigrateV2Data.Kinds)
            {
                rows.Add(format, kind, true);
                rows.Add(format, kind, false);
            }
        }

        return rows;
    }

    private static string Name(string kind, bool resolved) => $"{kind}-{(resolved ? "resolved" : "pending")}";

    private static string DecisionsFor(PersistedSession session) =>
        session.Pending.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(session.Pending.Select((_, i) => new { finding = i, action = "reject", reason = "the owner of the loop decides" }));

    private static string ErrorOf(string reply) => JsonDocument.Parse(reply).RootElement.TryGetProperty("error", out var e) ? e.GetString()! : string.Empty;

    private static string Bag(RoundRecord round) =>
        string.Join("|", (round.Unknown ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value.GetRawText())
            .Concat(round.ReviewerStates.Select(state => string.Join(",", (state.Unknown ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value.GetRawText())))));

    /// <summary>The migration, then the conversion's whole contract on one session: what changed, what did not, and what the next resolve does.</summary>
    private async Task AssertConvertedAndAdvancesOnce(string format, string kind, bool resolved)
    {
        MigrateV2Data.Plant(format, Name(kind, resolved), Data);
        var file = MigrateV2Data.SessionFile(Data, kind);
        var before = MigrateV2Data.Load(Data, file);
        LegacyHold.Stands(before.State).Should().BeTrue("the fixture is a hold a v1 build raised");

        var report = MigrateV2Data.Apply(Data);

        report.Status.Should().Be("done");
        report.Counts.Sessions.Should().Be(1, "one held session in the folder");
        var after = MigrateV2Data.Load(Data, file);
        LegacyHold.Stands(after.State).Should().BeFalse();
        after.State.Unknown?.Keys.Intersect(LegacyHold.Members).Should().BeNullOrEmpty("every hold member is dropped");
        after.State.AwaitingResolve.Should().BeTrue("the next resolve, and no other call, moves the stage");
        after.State.AdvanceOnResolve.Should().BeTrue();
        after.State.Stage.Should().Be(before.State.Stage, "the stage moves in resolve, through the one path that records the decisions");
        after.State.RoundsRunThisStage.Should().Be(before.State.RoundsRunThisStage, "the round count is kept");
        after.State.Rejections.Should().BeEquivalentTo(before.State.Rejections);
        after.Pending.Should().BeEquivalentTo(before.Pending, "pending findings are kept");
        after.Rounds.Should().HaveCount(before.Rounds.Count);
        after.Rounds[^1].Verdict.Should().Be(before.Rounds[^1].Verdict, "the verdict text is what it was");
        after.Rounds[^1].Note.Should().Be(MigrationV2.ConversionNote);
        // The unknown-member bags are compared by their raw text: a JsonElement from one file never equals one from another, and since v2 E3 removed the
        // cadence members from RoundRecord a v1 code or feature round carries them (planKey, epicNumber, cadenceNote) in exactly such a bag.
        after.Rounds[..^1].Should().BeEquivalentTo(before.Rounds[..^1], options => options.Excluding(member => member.Name == "Unknown"), "only the held round is touched");
        after.Rounds[..^1].Select(Bag).Should().Equal(before.Rounds[..^1].Select(Bag), "and what a newer or older build wrote into a round is kept as it was");

        var service = MigrateV2Data.Service(Data);
        var state = after.State;
        var reply = await service.ResolveAsync(MigrateV2Data.Repo, state.Branch, DecisionsFor(after), state.Document, state.Feature);
        ErrorOf(reply).Should().BeEmpty(reply);
        var advanced = MigrateV2Data.Load(Data, file);
        advanced.State.Stage.Should().Be(Stages.Of(before.State.Stage).AdvancesTo, "one resolve advances the stage");
        advanced.State.AwaitingResolve.Should().BeFalse();
        advanced.State.AdvanceOnResolve.Should().BeFalse();

        ErrorOf(await service.ResolveAsync(MigrateV2Data.Repo, state.Branch, "[]", state.Document, state.Feature))
            .Should().NotBeEmpty("a second resolve is refused: the stage moved once, not twice");
        MigrateV2Data.Load(Data, file).State.Stage.Should().Be(advanced.State.Stage);
    }

    // ---------- scenario 1 ----------

    [Theory]
    [InlineData("f1")]
    [InlineData("f2")]
    [InlineData("f3")]
    public async Task S1_AHeldPlanSession_WithItsFindingsResolved_LeavesTheHold_AndOneEmptyResolveAdvancesIt(string format) =>
        await AssertConvertedAndAdvancesOnce(format, "plan", resolved: true);

    // ---------- scenario 2 ----------

    [Theory]
    [InlineData("f1")]
    [InlineData("f2")]
    [InlineData("f3")]
    public async Task S2_AHeldPlanSession_WithPendingFindings_KeepsThem_AndOneResolveAdvancesOnce(string format)
    {
        await AssertConvertedAndAdvancesOnce(format, "plan", resolved: false);
        MigrateV2Data.Load(Data, MigrateV2Data.SessionFile(Data, "plan")).Pending.Should().BeEmpty("the one resolve decided them");
    }

    // ---------- scenario 3 ----------

    [Theory]
    [MemberData(nameof(EveryHold))]
    public async Task S3_EveryKindOfHold_InEveryFileShape_LeavesTheHold_AndAdvancesOnce(string format, string kind, bool resolved) =>
        await AssertConvertedAndAdvancesOnce(format, kind, resolved);

    // ---------- scenario 4 ----------

    [Theory]
    [InlineData("absent")]
    [InlineData("false")]
    [InlineData("true")]
    public void S4_HumanGateTrue_Stands_WhateverConfigAutonomousSays(string autonomous)
    {
        MigrateV2Data.Plant("f3", "plan-pending", Data);
        var file = MigrateV2Data.SessionFile(Data, "plan");
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        var config = json["state"]!["config"]!.AsObject();
        config.Remove("autonomous");
        if (autonomous != "absent")
        {
            config["autonomous"] = autonomous == "true";
        }

        File.WriteAllText(file, json.ToJsonString());
        LegacyHold.Stands(MigrateV2Data.Load(Data, file).State).Should().BeTrue($"humanGate stands with config.autonomous {autonomous}");

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Sessions.Should().Be(1, $"config.autonomous {autonomous} is not a reason to leave a hold");
        LegacyHold.Stands(MigrateV2Data.Load(Data, file).State).Should().BeFalse();
    }

    // ---------- scenario 5 ----------

    [Theory]
    [InlineData("plan-not-held")]
    [InlineData("plan-auto-released")]
    public void S5_ASessionThatIsNotHeld_OrWasReleasedByV1Auto_IsLeftByteForByte(string name)
    {
        MigrateV2Data.Plant("f3", name, Data);
        var before = MigrateV2Data.Tree(Data);
        var file = MigrateV2Data.SessionFile(Data, "plan");
        LegacyHold.Stands(MigrateV2Data.Load(Data, file).State).Should().BeFalse("the fixture holds nothing");

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Sessions.Should().Be(0);
        MigrateV2Data.Tree(Data).Where(e => e.Key.StartsWith("sessions/", StringComparison.Ordinal))
            .Should().Equal(before.Where(e => e.Key.StartsWith("sessions/", StringComparison.Ordinal)), "the session file is not even rewritten");
    }

    // ---------- scenario 15 ----------

    [Fact]
    public void S15_ASessionFileNamedDifferentlyFromItsKey_IsRewrittenWhereItLies_AndNoSecondFileAppears()
    {
        MigrateV2Data.Plant("f3", "plan-pending", Data);
        var key = MigrateV2Data.SessionFile(Data, "plan");
        var odd = Path.Combine(Data, "sessions", "session-0123456789abcdef.json");
        File.Move(key, odd);

        var report = MigrateV2Data.Apply(Data);

        report.Counts.Sessions.Should().Be(1);
        Directory.GetFiles(Path.Combine(Data, "sessions"), "*.json").Should().ContainSingle().Which.Should().Be(odd, "rewritten in place — a second file would be a second session");
        LegacyHold.Stands(MigrateV2Data.Load(Data, odd).State).Should().BeFalse();
        File.Exists(key).Should().BeFalse();
    }

    // ---------- scenario 20 ----------

    [Fact]
    public void S20_HumanGateIsNotRetired_SoAnOrdinarySaveBeforeTheMigrationKeepsTheEvidence()
    {
        RetiredMembers.Shipped.Of(typeof(SessionState)).Should().BeEmpty("retiring humanGate before the migration ran would lose the evidence of every hold");
        MigrateV2Data.Plant("f3", "plan-pending", Data);
        var file = MigrateV2Data.SessionFile(Data, "plan");
        var store = new SessionStore(Data);
        var loaded = store.LoadFile(file)!;

        store.Save(loaded with { Rounds = loaded.Rounds }); // an ordinary save, as any call of a v2 server makes

        File.ReadAllText(file).Should().Contain("humanGate");
        LegacyHold.Stands(store.LoadFile(file)!.State).Should().BeTrue("the hold evidence survived an ordinary save");
        MigrateV2Data.Apply(Data).Counts.Sessions.Should().Be(1, "so the migration still finds the hold afterwards");
    }
}
