using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The settings half of the v2 migration (E1.S4, scenario 16): keys v2 does not read leave <c>settings.json</c> for <c>settings.v1-removed.json</c> — under the
/// extension's own <c>settings.lock</c> — and every other key stays exactly as it was.
/// </summary>
public sealed class MigrateV2SettingsTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-migrate-v2-");

    private string Data => _dir.Path;

    private string Settings => Path.Combine(Data, "settings.json");

    private string Removed => Path.Combine(Data, "settings.v1-removed.json");

    private string Lock => Path.Combine(Data, "settings.lock");

    public void Dispose() => _dir.Dispose();

    private static JsonObject Read(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    private static readonly MigrationV2Tables Renaming = new(["COAI_GONE"], [("COAI_OLD", "COAI_NEW"), ("COAI_OLDER", "COAI_NEWER")], []);

    // ---------- the keys ----------

    [Fact]
    public void S16_RemovedKeys_GoToTheRemovedFile_WithTheirValueKindsKept_AndEveryOtherKeyIsUnchanged()
    {
        File.WriteAllText(Settings, """
            {
              "COAI_PROVIDERS": "codex,gemini",
              "COAI_ON_EXHAUSTED": "human",
              "COAI_MAX_ROUNDS": 3,
              "COAI_ESCALATION_MINUTES": 30,
              "COAI_ESCALATION_SECONDS": true,
              "COAI_ROUNDS": {"plan": 2},
              "COAI_LOG_LEVEL": "debug"
            }
            """);

        var report = MigrateV2Data.Apply(Data);

        report.Counts.SettingsRemoved.Should().Be(3);
        var kept = Read(Settings);
        kept.Select(m => m.Key).Should().Equal("COAI_PROVIDERS", "COAI_MAX_ROUNDS", "COAI_ROUNDS", "COAI_LOG_LEVEL");
        kept["COAI_PROVIDERS"]!.GetValue<string>().Should().Be("codex,gemini");
        kept["COAI_MAX_ROUNDS"]!.GetValue<int>().Should().Be(3);
        kept["COAI_ROUNDS"]!.ToJsonString().Should().Be("""{"plan":2}""");
        var removed = Read(Removed);
        removed["COAI_ON_EXHAUSTED"]!.GetValueKind().Should().Be(JsonValueKind.String);
        removed["COAI_ON_EXHAUSTED"]!.GetValue<string>().Should().Be("human");
        removed["COAI_ESCALATION_MINUTES"]!.GetValueKind().Should().Be(JsonValueKind.Number, "a number stays a number");
        removed["COAI_ESCALATION_MINUTES"]!.GetValue<int>().Should().Be(30);
        removed["COAI_ESCALATION_SECONDS"]!.GetValueKind().Should().Be(JsonValueKind.True, "a boolean stays a boolean");
    }

    [Fact]
    public void S16_AKeyTheRemovedFileAlreadyHas_IsNeverOverwritten()
    {
        File.WriteAllText(Settings, """{"COAI_ON_EXHAUSTED": "escalate", "COAI_MAX_ROUNDS": 3}""");
        File.WriteAllText(Removed, """{"COAI_ON_EXHAUSTED": "the first value", "COAI_SOMETHING_ELSE": 1}""");

        MigrateV2Data.Apply(Data);

        var removed = Read(Removed);
        removed["COAI_ON_EXHAUSTED"]!.GetValue<string>().Should().Be("the first value", "an existing key is never overwritten");
        removed["COAI_SOMETHING_ELSE"]!.GetValue<int>().Should().Be(1, "the merge keeps what was there");
        Read(Settings).Select(m => m.Key).Should().Equal("COAI_MAX_ROUNDS");
    }

    [Fact]
    public void S16_ARenamedKey_IsRewrittenUnderTheNewName_WhereItStood()
    {
        File.WriteAllText(Settings, """{"COAI_A": 1, "COAI_OLD": "value", "COAI_Z": 2}""");

        var report = MigrateV2Data.Apply(Data, Renaming);

        report.Counts.SettingsRenamed.Should().Be(1);
        var settings = Read(Settings);
        settings.Select(m => m.Key).Should().Equal("COAI_A", "COAI_NEW", "COAI_Z");
        settings["COAI_NEW"]!.GetValue<string>().Should().Be("value");
        File.Exists(Removed).Should().BeFalse("a plain rename moves nothing out");
    }

    [Fact]
    public void S16_WhenBothNamesExist_TheNewKeyWins_AndTheOldValueGoesToTheRemovedFile()
    {
        File.WriteAllText(Settings, """{"COAI_OLD": "stale", "COAI_NEW": "chosen", "COAI_GONE": 7}""");

        var report = MigrateV2Data.Apply(Data, Renaming);

        report.Counts.SettingsRenamed.Should().Be(1);
        report.Counts.SettingsRemoved.Should().Be(1);
        Read(Settings).Should().HaveCount(1).And.ContainKey("COAI_NEW");
        Read(Settings)["COAI_NEW"]!.GetValue<string>().Should().Be("chosen", "the new key wins");
        var removed = Read(Removed);
        removed["COAI_OLD"]!.GetValue<string>().Should().Be("stale", "the old value is kept where a person can find it");
        removed["COAI_GONE"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void S16_ASettingsFileThatIsNotJson_IsLeftUntouched_AndReportedUnreadable()
    {
        File.WriteAllText(Settings, "COAI_ON_EXHAUSTED=human\n");

        var report = MigrateV2Data.Apply(Data);

        File.ReadAllText(Settings).Should().Be("COAI_ON_EXHAUSTED=human\n");
        File.Exists(Removed).Should().BeFalse();
        report.Counts.Unreadable.Should().Be(1);
        report.Status.Should().Be("done", "unreadable is reported, not blocking");
    }

    [Fact]
    public void S16_AFolderWithNoSettingsFile_HasNothingToMove()
    {
        var report = MigrateV2Data.Apply(Data);

        report.Status.Should().Be("done", "a folder that never had settings has nothing deferred and nothing unreadable");
        report.Counts.Should().BeEquivalentTo(new MigrateV2Counts(0, 0, 0, 0, 0, 0, 0));
        File.Exists(Settings).Should().BeFalse("the migration never makes a settings file");
        File.Exists(Removed).Should().BeFalse();
    }

    // ---------- the lock ----------

    [Fact]
    public void S16_WhileAnotherWindowHoldsTheSettingsLock_TheKeysAreDeferred_AndTheNextRunMovesThem()
    {
        File.WriteAllText(Settings, """{"COAI_ON_EXHAUSTED": "human", "COAI_MAX_ROUNDS": 3}""");
        var original = File.ReadAllBytes(Settings);
        File.WriteAllText(Lock, "4242:1:the-other-window");

        var report = MigrateV2Data.Apply(Data);

        report.Status.Should().Be("partial");
        report.Counts.Deferred.Should().Be(1);
        File.ReadAllBytes(Settings).Should().Equal(original, "the window that holds the lock is writing this file");
        File.Exists(Removed).Should().BeFalse();
        File.ReadAllText(Lock).Should().Be("4242:1:the-other-window", "somebody else's lock is not ours to delete");
        File.Exists(MigrationV2.MarkerFile(Data)).Should().BeFalse("deferred work keeps the marker away");

        File.Delete(Lock);
        var next = MigrateV2Data.Apply(Data);

        next.Status.Should().Be("done");
        next.Counts.SettingsRemoved.Should().Be(1);
        Read(Settings).Select(m => m.Key).Should().Equal("COAI_MAX_ROUNDS");
        File.Exists(Lock).Should().BeFalse("the migration releases the lock it took");
    }

    [Fact]
    public void S16_ALockOlderThanTenSeconds_BelongedToAWindowThatDied_AndIsBroken()
    {
        File.WriteAllText(Settings, """{"COAI_ON_EXHAUSTED": "human"}""");
        File.WriteAllText(Lock, "4242:1:a-dead-window");
        File.SetLastWriteTimeUtc(Lock, DateTime.UtcNow.AddSeconds(-30));

        var report = MigrateV2Data.Apply(Data);

        report.Status.Should().Be("done");
        report.Counts.SettingsRemoved.Should().Be(1);
        File.Exists(Lock).Should().BeFalse();
    }

    [Fact]
    public void S16_ALockWrittenInTheFuture_IsNotStale_BecauseAClockThatSteppedBackCannotTellHowOldItIs()
    {
        File.WriteAllText(Settings, """{"COAI_ON_EXHAUSTED": "human"}""");
        File.WriteAllText(Lock, "4242:1:a-window");
        File.SetLastWriteTimeUtc(Lock, DateTime.UtcNow.AddMinutes(5));

        var report = MigrateV2Data.Apply(Data);

        report.Status.Should().Be("partial");
        File.ReadAllText(Settings).Should().Contain("COAI_ON_EXHAUSTED");
    }

    [Fact]
    public void S16_StaleMeansOlderThanTenSecondsAndNeverNegative_AsTheExtensionDecides()
    {
        var now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

        SettingsLockFile.IsStale(now, now).Should().BeFalse("this instant");
        SettingsLockFile.IsStale(now.AddSeconds(-10), now).Should().BeFalse("exactly at the boundary");
        SettingsLockFile.IsStale(now.AddSeconds(-10).AddMilliseconds(-1), now).Should().BeTrue();
        SettingsLockFile.IsStale(now.AddSeconds(10), now).Should().BeFalse("a negative age is not stale");
    }
}
