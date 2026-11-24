using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What coai v2 E3 adds to the E1 migration: the settings keys the server stopped reading and the ones it renamed (E3.S2, E3.S3 — rows of <see cref="MigrationV2Tables"/>),
/// and the start-up sweep that archives an answered or expired card 30 days old (E1.S4 SPEC risk 7, decided there for E3).
/// </summary>
public sealed class MigrateV2E3Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The tables E1 shipped: the marker an earlier build wrote carries THEIR hash.</summary>
    private static readonly MigrationV2Tables E1Tables = new(["COAI_ON_EXHAUSTED", "COAI_ESCALATION_MINUTES", "COAI_ESCALATION_SECONDS"], [], ["qphase", "cadence"]);

    private readonly TempDir _dir = TempDir.For("coai-migrate-v2-e3-");

    private string Data => _dir.Path;

    private string Settings => Path.Combine(Data, "settings.json");

    private string Removed => Path.Combine(Data, "settings.v1-removed.json");

    public void Dispose() => _dir.Dispose();

    private static JsonObject Read(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    // ---------- the settings tables ----------

    private const string V1Settings = """
        {
          "COAI_PROVIDERS": "codex,gemini",
          "COAI_AUTONOMOUS": true,
          "COAI_COMMANDS": "[{\"name\":\"x\"}]",
          "COAI_COMMAND_MODELS": "{}",
          "COAI_SPLIT_PLAN": true,
          "COAI_SPLIT_WITH_FABLE": false,
          "COAI_GATE_PER": "task",
          "COAI_STOP_LOCAL_WHEN_QUIET": true,
          "COAI_DEAL_PLAN": true,
          "COAI_DEAL_CODE": true,
          "COAI_ROTATE_PROMPTS": true,
          "COAI_PROMPTS_PER_ROUND": "{\"plan\":2}",
          "COAI_SECURITY_LANE": "on",
          "COAI_QCONSULT_MODE": "free",
          "COAI_QCONSULT_FREE_BATCHES": 3,
          "COAI_CADENCE_MODE": "require",
          "COAI_CADENCE_EVERY": 3,
          "COAI_CADENCE_RISK_THRESHOLD": 6,
          "COAI_CADENCE_RISK_MAX": 2,
          "COAI_CONSULT_ENABLED": "0",
          "COAI_CONSULT_TURNS": 7,
          "COAI_CONSULT_CALLS_PER_SESSION": 12,
          "COAI_CONSULT_IDLE_MINUTES": 20,
          "COAI_QCONSULT_ENABLED": "1",
          "COAI_QCONSULT_ROWS": "[\"codex\"]",
          "COAI_QCONSULT_PROMPTS": "{}",
          "COAI_QCONSULT_ROOTS": "[\"/work\"]",
          "COAI_QCONSULT_ROW_MINUTES": 9,
          "COAI_QCONSULT_QUESTIONS_PER_SESSION": 4,
          "COAI_MAX_ROUNDS": 3
        }
        """;

    private static readonly string[] RemovedByE3 =
    [
        "COAI_AUTONOMOUS", "COAI_COMMANDS", "COAI_COMMAND_MODELS", "COAI_SPLIT_PLAN", "COAI_SPLIT_WITH_FABLE", "COAI_GATE_PER", "COAI_STOP_LOCAL_WHEN_QUIET",
        "COAI_DEAL_PLAN", "COAI_DEAL_CODE", "COAI_ROTATE_PROMPTS", "COAI_PROMPTS_PER_ROUND", "COAI_SECURITY_LANE", "COAI_QCONSULT_MODE", "COAI_QCONSULT_FREE_BATCHES",
        "COAI_CADENCE_MODE", "COAI_CADENCE_EVERY", "COAI_CADENCE_RISK_THRESHOLD", "COAI_CADENCE_RISK_MAX",
    ];

    [Fact]
    public void E3_AV1SettingsFile_MovesTheRemovedKeysAside_AndRenamesTheOthers_ExactlyOnce()
    {
        File.WriteAllText(Settings, V1Settings);

        var first = MigrateV2Data.Apply(Data);

        first.Counts.SettingsRemoved.Should().Be(RemovedByE3.Length);
        first.Counts.SettingsRenamed.Should().Be(10);
        var settings = Read(Settings);
        settings.Select(m => m.Key).Should().Equal(
            "COAI_PROVIDERS", "COAI_CONS_ENABLED", "COAI_CONS_TURNS", "COAI_CONS_CALLS_PER_SESSION", "COAI_CONS_IDLE_MINUTES",
            "COAI_CONSASK_ENABLED", "COAI_CONSASK_ROWS", "COAI_CONSASK_PROMPTS", "COAI_CONSASK_ROOTS", "COAI_CONSASK_ROW_MINUTES", "COAI_CONSASK_QUESTIONS_PER_SESSION",
            "COAI_MAX_ROUNDS");
        settings["COAI_CONS_TURNS"]!.GetValue<int>().Should().Be(7, "a renamed key keeps its value and its kind");
        settings["COAI_CONSASK_ROOTS"]!.GetValue<string>().Should().Be("[\"/work\"]");
        var removed = Read(Removed);
        removed.Select(m => m.Key).Should().BeEquivalentTo(RemovedByE3, "every removed key — and no renamed one — is in the removed file");
        removed["COAI_AUTONOMOUS"]!.GetValue<bool>().Should().BeTrue();
        removed["COAI_CADENCE_EVERY"]!.GetValue<int>().Should().Be(3);
        removed["COAI_GATE_PER"]!.GetValue<string>().Should().Be("task");

        var settingsBytes = File.ReadAllBytes(Settings);
        var removedBytes = File.ReadAllBytes(Removed);
        var second = MigrateV2Data.Apply(Data);

        second.Counts.SettingsRemoved.Should().Be(0, "exactly once: the second run finds nothing to move");
        second.Counts.SettingsRenamed.Should().Be(0);
        File.ReadAllBytes(Settings).Should().Equal(settingsBytes);
        File.ReadAllBytes(Removed).Should().Equal(removedBytes);
    }

    [Fact]
    public void E3_WhenTheNewNameIsThereToo_TheNewValueWins_AndTheOldOneIsKept()
    {
        File.WriteAllText(Settings, """{"COAI_CONSULT_TURNS": 7, "COAI_CONS_TURNS": 2, "COAI_QCONSULT_ENABLED": "0", "COAI_CONSASK_ENABLED": "1"}""");

        MigrateV2Data.Apply(Data);

        var settings = Read(Settings);
        settings["COAI_CONS_TURNS"]!.GetValue<int>().Should().Be(2, "the new key wins");
        settings["COAI_CONSASK_ENABLED"]!.GetValue<string>().Should().Be("1");
        settings.Should().HaveCount(2);
        var removed = Read(Removed);
        removed["COAI_CONSULT_TURNS"]!.GetValue<int>().Should().Be(7, "the old value is kept where a person can find it");
        removed["COAI_QCONSULT_ENABLED"]!.GetValue<string>().Should().Be("0");
    }

    [Fact]
    public void E3_TheTablesHashMovedWithTheRows_SoAFolderMigratedByTheE1TablesRunsAgainAtTheNextStart()
    {
        MigrationV2Tables.Default.Hash.Should().NotBe(E1Tables.Hash, "the E3 rows are part of the hash in the marker");
        File.WriteAllText(Settings, """{"COAI_ON_EXHAUSTED": "human", "COAI_MAX_ROUNDS": 3}""");
        MigrateV2Data.Apply(Data, E1Tables).Status.Should().Be("done");
        MigrationV2.MarkerHash(Data).Should().Be(E1Tables.Hash);

        // v1 extension 0.89.x rewrites settings.json at each activation (SPEC risk 3): the removed keys are back.
        File.WriteAllText(Settings, """{"COAI_CADENCE_MODE": "require", "COAI_CONSULT_TURNS": 9, "COAI_MAX_ROUNDS": 3}""");

        MigrateV2Mode.RunAutomatic(Data, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(Data));

        MigrationV2.MarkerHash(Data).Should().Be(MigrationV2Tables.Default.Hash);
        Read(Settings).Select(m => m.Key).Should().Equal("COAI_CONS_TURNS", "COAI_MAX_ROUNDS");
        Read(Removed)["COAI_CADENCE_MODE"]!.GetValue<string>().Should().Be("require");
        Read(Removed)["COAI_ON_EXHAUSTED"]!.GetValue<string>().Should().Be("human", "what the first run moved is still there");
    }

    // ---------- the 30-day sweep of answered and expired cards ----------

    private string Cards => Path.Combine(Data, "escalations");

    private string Archive => Path.Combine(Cards, "archive");

    private MigrationV2 At(DateTimeOffset now) => new(Data, _ => { }, MigrateV2Data.Sink(Data), clock: new FixedClock(now));

    /// <summary>An answered card (and its answer) asked 40 days before <see cref="Now"/> and answered <paramref name="answeredDaysAgo"/> days before it.</summary>
    private string PlantAnswered(string name, int answeredDaysAgo)
    {
        MigrateV2Data.Plant("f3", name, Data, sessions: false);
        foreach (var file in Directory.GetFiles(Cards, "*.json"))
        {
            var answer = file.EndsWith(".answer.json", StringComparison.Ordinal);
            MigrateV2Data.Stamp(file, Now.AddDays(answer ? -answeredDaysAgo : -40));
        }

        return Directory.GetFiles(Cards, "*.json").Single(f => f.EndsWith("20ef85d344a0.json", StringComparison.Ordinal));
    }

    [Fact]
    public void E3_AnAnsweredCard_ThirtyDaysAfterItsAnswer_IsArchivedWithItsAnswerFile_ByTheStartSweep_AndOnlyThen()
    {
        var card = PlantAnswered("plan-answered", answeredDaysAgo: 31);
        var answer = Path.Combine(Cards, "20ef85d344a0.answer.json");
        var before = File.ReadAllBytes(card);

        var report = At(Now).SweepCards();

        report.Status.Should().Be("done");
        report.Counts.Cards.Should().Be(1);
        File.Exists(card).Should().BeFalse();
        File.Exists(answer).Should().BeFalse("the answer moves with its card");
        File.ReadAllBytes(Path.Combine(Archive, "20ef85d344a0.json")).Should().Equal(before, "an aged card is moved, never rewritten");
        File.Exists(Path.Combine(Archive, "20ef85d344a0.answer.json")).Should().BeTrue();
        MigrationV2Journal.Read(MigrationV2.JournalFile(Data)).Should().Contain(l => l.Kind == "card" && l.Id == "20ef85d344a0" && l.State == "done");

        var tree = MigrateV2Data.Tree(Data);
        At(Now).SweepCards().Counts.Cards.Should().Be(0, "a second sweep finds nothing");
        MigrateV2Data.Tree(Data).Should().Equal(tree);
    }

    [Fact]
    public void E3_AnAnsweredCard_AnsweredTwentyNineDaysAgo_StaysWhereItIs_EvenWhenItWasAskedMuchEarlier()
    {
        var card = PlantAnswered("plan-answered", answeredDaysAgo: 29);

        var report = At(Now).SweepCards();

        report.Counts.Cards.Should().Be(0);
        File.Exists(card).Should().BeTrue("the age is the answer's, not the question's");
        Directory.Exists(Archive).Should().BeFalse();
    }

    [Fact]
    public void E3_AnExpiredCard_IsArchivedBy_ItsExpiryDate_AtThirtyDays_AndAYoungerOneStays()
    {
        MigrateV2Data.Plant("f3", "plan-auto-released", Data, sessions: false);
        var card = Directory.GetFiles(Cards, "*.json").Single();
        MigrateV2Data.Stamp(card, Now.AddDays(-30));

        At(Now.AddHours(-1)).SweepCards().Counts.Cards.Should().Be(0, "29 days and 23 hours is not 30 days");
        File.Exists(card).Should().BeTrue();

        At(Now).SweepCards().Counts.Cards.Should().Be(1);
        File.Exists(card).Should().BeFalse();
        File.Exists(Path.Combine(Archive, Path.GetFileName(card))).Should().BeTrue();
    }

    [Fact]
    public void E3_AnOldCardWhoseHeldSessionStillStands_StaysWhereTheSessionFindsIt_AndAnOldCardWithAnUnreadableDateStays()
    {
        MigrateV2Data.Plant("f3", "plan-pending", Data); // a held session and its open card
        var held = Directory.GetFiles(Cards, "*.json").Single();
        var id = Path.GetFileNameWithoutExtension(held);
        File.WriteAllText(Path.Combine(Cards, id + ".answer.json"), """{"id":"x","answer":"Fix the findings","decision":"fix","answeredUtc":"2026-01-01T00:00:00Z"}""");
        var odd = JsonNode.Parse(File.ReadAllText(held))!.AsObject();
        odd["id"] = "aaaaaaaaaaaa";
        odd["sessionId"] = "no-such-session";
        odd["status"] = "expired";
        odd["expiredUtc"] = "when the moon is full";
        odd["askedUtc"] = "never";
        File.WriteAllText(Path.Combine(Cards, "aaaaaaaaaaaa.json"), odd.ToJsonString());

        var report = At(Now).SweepCards();

        report.Counts.Cards.Should().Be(0);
        File.Exists(held).Should().BeTrue("a held session names this answered card: its conversion announces it first");
        File.Exists(Path.Combine(Cards, "aaaaaaaaaaaa.json")).Should().BeTrue("a date nobody can read is no age");
        LegacyHold.Stands(MigrateV2Data.Load(Data, MigrateV2Data.SessionFile(Data, "plan")).State).Should().BeTrue("a sweep never touches a session");
    }

    [Fact]
    public void E3_TheFullMigration_ArchivesAnOldCardToo_AndTheDryRunCountsIt()
    {
        PlantAnswered("plan-answered", answeredDaysAgo: 45);
        MigrateV2Data.Plant("f3", "plan-pending", Data, sessions: false);
        var open = Directory.GetFiles(Cards, "*.json").Single(f => !f.EndsWith("20ef85d344a0.json", StringComparison.Ordinal) && !f.EndsWith(".answer.json", StringComparison.Ordinal));
        MigrateV2Data.Stamp(open, Now);

        var plan = new MigrationV2(Data, _ => { }, _ => true, clock: new FixedClock(Now)).Plan();
        plan.Counts.Cards.Should().Be(2, "the open card to withdraw and the old answered one to archive");
        plan.Steps.Should().Contain(s => s.Id == "20ef85d344a0" && s.Action == "archive");

        var applied = At(Now).Apply();

        applied.Counts.Cards.Should().Be(2);
        File.Exists(Path.Combine(Archive, "20ef85d344a0.json")).Should().BeTrue();
        File.Exists(Path.Combine(Archive, Path.GetFileName(open))).Should().BeTrue();
    }
}
