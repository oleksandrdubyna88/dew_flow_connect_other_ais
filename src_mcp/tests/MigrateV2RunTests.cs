using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using CoaiMcp.Server.Migrations;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The migration as a whole run (E1.S4, scenarios 8–14, 17 and 18): killed at every boundary, run twice, run dry, run by two processes, run on a folder that cannot
/// be written, over a corrupt file, over a session somebody holds, over a store that comes back, and gated by version.
/// </summary>
public sealed class MigrateV2RunTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-migrate-v2-");

    private int _folders;

    public void Dispose() => _root.Dispose();

    /// <summary>A new folder of the mixed data every run test starts from.</summary>
    private string Folder(bool mixed = true)
    {
        var dir = Path.Combine(_root.Path, "data" + _folders++);
        Directory.CreateDirectory(dir);
        if (mixed)
        {
            MigrateV2Data.Mixed(dir);
        }

        return dir;
    }

    // ---------- scenario 8 ----------

    [Fact]
    public void S8_AKillAtEveryBoundary_ThenOneCleanRun_LeavesTheTreeOfOneCleanRun()
    {
        var reference = Folder();
        var boundaries = 0;
        var clean = MigrateV2Data.Migration(reference, atBoundary: _ => boundaries++).Apply();
        clean.Status.Should().Be("done");
        clean.Counts.Should().BeEquivalentTo(new MigrateV2Counts(3, 4, 2, 3, 0, 0, 0));
        var expected = MigrateV2Data.Tree(reference, withFolders: true);
        boundaries.Should().BeGreaterThan(40, "four boundaries around every object, and one before the marker");

        for (var k = 0; k < boundaries; k++)
        {
            var dir = Folder();
            var killed = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (n == k) { throw new SimulatedKill(k); } }).Apply();
            killed.Should().Throw<SimulatedKill>($"boundary {k} of {boundaries}");

            var resumed = MigrateV2Data.Apply(dir);

            resumed.Status.Should().Be("done", $"after a kill at boundary {k}");
            MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(expected, $"a kill at boundary {k} then a clean run equals one clean run");
            File.Exists(MigrationV2.MarkerFile(dir)).Should().BeTrue($"the marker is written by the run that finished, after a kill at {k}");
        }
    }

    [Fact]
    public void S8_ASessionKilledAfterItsWrite_AndBeforeItsDoneLine_GetsItsDatabaseProjectionAgain()
    {
        var dir = Folder(mixed: false);
        MigrateV2Data.Plant("f3", "plan-pending", dir);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var state = MigrateV2Data.Load(dir, file).State;
        SeedDatabase(dir, MigrateV2Data.Load(dir, file));
        var killed = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (n == 2) { throw new SimulatedKill(n); } }).Apply();
        killed.Should().Throw<SimulatedKill>();
        MigrateV2Data.Load(dir, file).Rounds[^1].Note.Should().Be(MigrationV2.ConversionNote, "boundary 2 is after the session's write");
        NoteInDatabase(dir, state.SessionId).Should().BeEmpty("the kill came before the projection");

        var resumed = MigrateV2Data.Apply(dir);

        resumed.Status.Should().Be("done");
        resumed.Counts.Sessions.Should().Be(0, "the session was converted by the killed run; this run only projects");
        NoteInDatabase(dir, state.SessionId).Should().Be(MigrationV2.ConversionNote, "a conversion note with no done line is written into coai.db again");
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Should().Contain(l => l.Kind == "session" && l.State == "done");

        var third = MigrateV2Data.Apply(dir);
        third.Steps.Should().BeEmpty("the done line is the journal's one use: nothing is projected a third time");
    }

    private static void SeedDatabase(string dir, PersistedSession session)
    {
        using var db = RoundsDb.OpenExisting(dir, Serilog.Core.Logger.None) ?? RoundsDb.Open(dir, Serilog.Core.Logger.None)!;
        db.RecordRound(session.State, session.Rounds[^1], [], new RoundContext(PlanText: "PLAN", HeadSha: "sha", Caller: "claude"));
    }

    private static string NoteInDatabase(string dir, string sessionId)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT note FROM rounds WHERE session_id = $s";
        read.Parameters.AddWithValue("$s", sessionId);

        return read.ExecuteScalar() as string ?? string.Empty;
    }

    [Fact]
    public void S8_ATornLastJournalLine_IsIgnoredByTheReader_AndCutAwayBeforeTheNextLine()
    {
        var dir = Folder();
        Directory.CreateDirectory(MigrationV2.MigrationsDir(dir));
        File.WriteAllText(MigrationV2.JournalFile(dir), """{"seq":1,"utc":"2026-11-22T10:00:00.0000000Z","kind":"store","id":"qphase","state":"planned","detail":""}""" + "\n{\"seq\":2,\"ut");

        var report = MigrateV2Data.Apply(dir);

        report.Status.Should().Be("done");
        var lines = MigrationV2Journal.Read(MigrationV2.JournalFile(dir));
        lines[0].Seq.Should().Be(1);
        lines.Should().Contain(l => l.Seq == 2 && l.Kind == "session" && l.State == "planned", "the first line this run wrote stands on its own line — it was not glued to the torn one");
        File.ReadAllText(MigrationV2.JournalFile(dir)).Should().NotContain("\"ut\n", "the torn bytes are cut away before the append (SPEC §5.5), not ended with a newline and kept");
        lines.Count.Should().BeGreaterThan(10, "every line after the torn one parsed");
        lines.Select(l => l.Seq).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    // ---------- scenario 9 ----------

    [Fact]
    public void S9_ASecondApply_ChangesNoByte_AndCountsNothing()
    {
        var dir = Folder();
        MigrateV2Data.Apply(dir).Status.Should().Be("done");
        var after = MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true);

        var second = MigrateV2Data.Apply(dir);

        second.Counts.Should().BeEquivalentTo(new MigrateV2Counts(0, 0, 0, 0, 0, 0, 0));
        second.Steps.Should().BeEmpty();
        MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true).Where(e => !e.Key.StartsWith("claims/", StringComparison.Ordinal))
            .Should().Equal(after.Where(e => !e.Key.StartsWith("claims/", StringComparison.Ordinal)), "not a byte: not the journal, not the marker");
    }

    [Fact]
    public void S9_ASecondApply_AfterTheJournalAndTheMarkerAreDeleted_ChangesNothingButThoseTwo()
    {
        var dir = Folder();
        MigrateV2Data.Apply(dir);
        var after = MigrateV2Data.Tree(dir, withFolders: true);
        File.Delete(MigrationV2.JournalFile(dir));
        File.Delete(MigrationV2.MarkerFile(dir));

        var second = MigrateV2Data.Apply(dir);

        second.Counts.Should().BeEquivalentTo(new MigrateV2Counts(0, 0, 0, 0, 0, 0, 0), "the objects say there is nothing to do, whatever the journal says");
        MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(after);
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeTrue("the run that found nothing left still says the folder is migrated");
    }

    // ---------- scenario 10 ----------

    [Fact]
    public void S10_ADryRun_CountsWhatAnApplyCounts_ChangesNoByte_AndCreatesNoFile()
    {
        var dry = Folder();
        var wet = Folder();
        var before = MigrateV2Data.Tree(dry, withFolders: true, withBookkeeping: true);

        var plan = MigrateV2Data.Plan(dry);

        MigrateV2Data.Tree(dry, withFolders: true, withBookkeeping: true).Should().Equal(before, "a dry run creates nothing: no migrations/, no lease, no claim, no turn, no journal");
        var applied = MigrateV2Data.Apply(wet);
        plan.Status.Should().Be("plan");
        plan.Apply.Should().BeFalse();
        applied.Apply.Should().BeTrue();
        plan.Counts.Should().BeEquivalentTo(applied.Counts, "the plan is the counts an apply reaches");
        plan.AnsweredHolds.Should().BeEquivalentTo(applied.AnsweredHolds);
        plan.Counts.Should().BeEquivalentTo(new MigrateV2Counts(3, 4, 2, 3, 0, 0, 0));
    }

    [Fact]
    public void S10_ThePlanNamesEveryStep_AndTheModeAnswersItAsOneObjectWithExitZero()
    {
        var dir = Folder();
        var before = MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true);

        var (code, output, err) = MigrateV2Data.Mode(dir);

        code.Should().Be(0, err);
        var report = MigrateV2Data.Report(output);
        report.GetProperty("mode").GetString().Should().Be("migrate-v2");
        report.GetProperty("apply").GetBoolean().Should().BeFalse();
        report.GetProperty("status").GetString().Should().Be("plan");
        report.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("kind").GetString()).Distinct()
            .Should().BeEquivalentTo("session", "card", "store", "settings");
        MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true).Should().Equal(before);
    }

    // ---------- scenario 11 ----------

    [Fact]
    public async Task S11_WhileAnotherMigrationHoldsTheLease_TheRealBinaryAnswers75Busy_AndChangesNothing()
    {
        var dir = Folder();
        var reference = Folder();
        MigrateV2Data.Apply(reference);
        var before = MigrateV2Data.Tree(dir, withFolders: true);
        Directory.CreateDirectory(Path.Combine(dir, "claims"));
        var other = HeldFile.TryHold(MigrationV2Lease.FileFor(dir));
        other.Should().NotBeNull("this test is the other migration");
        try
        {
            var (code, output, _) = await MigrateV2Data.Binary(dir, "--migrate-v2", "--apply");

            code.Should().Be(75);
            MigrateV2Data.Report(output).GetProperty("status").GetString().Should().Be("busy");
            MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(before, "the loser touched nothing");
            File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse("no marker from a run that did not run");
        }
        finally
        {
            other!.Dispose();
        }

        var (second, _, err) = await MigrateV2Data.Binary(dir, "--migrate-v2", "--apply");

        second.Should().Be(0, err);
        MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(MigrateV2Data.Tree(reference, withFolders: true), "the result equals one run");
    }

    [Fact]
    public async Task S11_TwoRealProcessesAtOnce_NeverBothMigrate_AndTheFolderEqualsOneRun()
    {
        var dir = Folder();
        var reference = Folder();
        MigrateV2Data.Apply(reference);

        var results = await Task.WhenAll(
            MigrateV2Data.Binary(dir, "--migrate-v2", "--apply"),
            MigrateV2Data.Binary(dir, "--migrate-v2", "--apply"));

        results.Select(r => r.Code).Should().OnlyContain(c => c == 0 || c == 75, "one migrates; the other either waits its turn or answers busy");
        results.Select(r => r.Code).Should().Contain(0);
        var (third, _, err) = await MigrateV2Data.Binary(dir, "--migrate-v2", "--apply");
        third.Should().Be(0, err);
        MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(MigrateV2Data.Tree(reference, withFolders: true));
    }

    // ---------- scenario 12 ----------

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void S12_AFolderThatCannotBeWritten_AnswersADryRun0_AndAnApply74_ChangingNothing_AndTheServerStillStarts()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "a read-only folder is a Unix mode a privileged user ignores");
        var dir = Folder();
        var before = MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true);
        var mode = File.GetUnixFileMode(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var (planCode, planOut, planErr) = MigrateV2Data.Mode(dir);
            var (applyCode, applyOut, applyErr) = MigrateV2Data.Mode(dir, "--apply");

            planCode.Should().Be(0, planErr);
            MigrateV2Data.Report(planOut).GetProperty("status").GetString().Should().Be("plan");
            applyCode.Should().Be(74, applyErr);
            MigrateV2Data.Report(applyOut).GetProperty("status").GetString().Should().Be("failed");
            MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true).Should().Equal(before, "nothing changed");

            var warnings = new List<string>();
            var serve = () => MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), warnings.Add, MigrateV2Data.Sink(dir));
            serve.Should().NotThrow("the automatic run never throws into serve");
            warnings.Should().Contain(w => w.Contains("did not finish", StringComparison.Ordinal), "a warning, and the server starts anyway");
        }
        finally
        {
            File.SetUnixFileMode(dir, mode);
        }
    }

    // ---------- scenario 13 (rewritten by the epic gate: an unreadable object is an incomplete migration, SPEC §5.2 / §6.4) ----------

    [Fact]
    public void S13_ACorruptSessionFile_IsUnreadableAndUntouched_Exit75Partial_NoMarker_AndAfterTheRepairTheNextRunConvertsItAndWritesTheMarker()
    {
        var dir = Folder();
        var corrupt = Path.Combine(dir, "sessions", "session-ffffffffffffffff.json");
        File.WriteAllText(corrupt, """{"state": {"sessionId": "torn""");

        var (code, output, err) = MigrateV2Data.Mode(dir, "--apply");

        code.Should().Be(75, "an unreadable object is an incomplete migration, not a reported one: " + err);
        var report = MigrateV2Data.Report(output);
        report.GetProperty("status").GetString().Should().Be("partial");
        report.GetProperty("counts").GetProperty("unreadable").GetInt32().Should().Be(1);
        report.GetProperty("counts").GetProperty("sessions").GetInt32().Should().Be(3, "the other three held sessions migrated");
        File.ReadAllText(corrupt).Should().Be("""{"state": {"sessionId": "torn""", "an unreadable file is left exactly as it was");
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse("no marker: the next start looks again");
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Should().Contain(l => l.State == "unreadable" && l.Id == "session-ffffffffffffffff.json");

        // The file is repaired: here, a held code session the disk finished writing, under the same odd name.
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "migrate-v2", "f3", "code-resolved", "sessions", "session-2daeebe8b876dd85.json"),
            corrupt,
            overwrite: true);
        LegacyHold.Stands(MigrateV2Data.Load(dir, corrupt).State).Should().BeTrue();

        var (second, secondOut, secondErr) = MigrateV2Data.Mode(dir, "--apply");

        second.Should().Be(0, secondErr);
        var again = MigrateV2Data.Report(secondOut);
        again.GetProperty("counts").GetProperty("sessions").GetInt32().Should().Be(1, "the repaired file is converted");
        again.GetProperty("status").GetString().Should().Be("done");
        LegacyHold.Stands(MigrateV2Data.Load(dir, corrupt).State).Should().BeFalse();
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeTrue("the run that found nothing unreadable writes the marker");
    }

    [Theory]
    [InlineData("session")]
    [InlineData("card")]
    [InlineData("settings")]
    public void S13_AnUnreadableObjectOfAnyKind_MakesTheApplyPartial_Exit75_AndWritesNoMarker(string kind)
    {
        var dir = Folder();
        var file = kind switch
        {
            "session" => Path.Combine(dir, "sessions", "session-eeeeeeeeeeeeeeee.json"),
            "card" => Path.Combine(dir, "escalations", "zzzzzzzzzzzz.json"),
            _ => Path.Combine(dir, "settings.json"),
        };
        File.WriteAllText(file, "{ this is not json");

        var (code, output, err) = MigrateV2Data.Mode(dir, "--apply");

        code.Should().Be(75, err);
        MigrateV2Data.Report(output).GetProperty("status").GetString().Should().Be("partial");
        MigrateV2Data.Report(output).GetProperty("counts").GetProperty("unreadable").GetInt32().Should().Be(1);
        File.ReadAllText(file).Should().Be("{ this is not json");
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse();
    }

    // ---------- scenario 14 ----------

    [Fact]
    public void S14_ASessionWhoseClaimIsHeld_IsDeferred_Exit75_NoMarker_AndTheNextRunCompletes()
    {
        var dir = Folder(mixed: false);
        MigrateV2Data.Plant("f3", "plan-pending", dir);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var state = MigrateV2Data.Load(dir, file).State;
        var bytes = File.ReadAllBytes(file);
        var claim = SessionClaim.TryTake(dir, MigrateV2Data.Repo, state.Branch);
        claim.Should().NotBeNull("this test is the call that is changing the session");
        try
        {
            var (code, output, _) = MigrateV2Data.Mode(dir, "--apply");

            code.Should().Be(75);
            var report = MigrateV2Data.Report(output);
            report.GetProperty("status").GetString().Should().Be("partial");
            report.GetProperty("counts").GetProperty("deferred").GetInt32().Should().Be(1);
            File.ReadAllBytes(file).Should().Equal(bytes, "a session somebody is changing is not touched");
            File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse("no marker: the next start retries");
        }
        finally
        {
            claim!.Dispose();
        }

        var (second, secondOut, err) = MigrateV2Data.Mode(dir, "--apply");

        second.Should().Be(0, err);
        MigrateV2Data.Report(secondOut).GetProperty("counts").GetProperty("sessions").GetInt32().Should().Be(1);
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse();
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeTrue();
    }

    // ---------- scenario 17 ----------

    [Fact]
    public void S17_QphaseAndCadence_AreArchivedWithEveryFile_AndAReCreatedSourceMergesWithoutOverwriting()
    {
        var dir = Folder();
        var qphase = Directory.GetFiles(Path.Combine(dir, "qphase")).Order().Select(f => (Name: Path.GetFileName(f), Bytes: File.ReadAllBytes(f))).ToList();
        var cadence = File.ReadAllBytes(Path.Combine(dir, "cadence", "repo-1.json"));

        var first = MigrateV2Data.Apply(dir);

        first.Counts.Stores.Should().Be(2);
        Directory.Exists(Path.Combine(dir, "qphase")).Should().BeFalse();
        Directory.Exists(Path.Combine(dir, "cadence")).Should().BeFalse();
        var archive = Path.Combine(dir, "migrations", "v1-archive");
        foreach (var (name, bytes) in qphase)
        {
            File.ReadAllBytes(Path.Combine(archive, "qphase", name)).Should().Equal(bytes, "every file of the store is in the archive");
        }

        File.ReadAllBytes(Path.Combine(archive, "cadence", "repo-1.json")).Should().Equal(cadence);

        // The store comes back (cadence and qphase code stays live until E3): same name, other content, and a new file.
        Directory.CreateDirectory(Path.Combine(dir, "qphase", "deeper"));
        File.WriteAllText(Path.Combine(dir, "qphase", "plan-a.json"), """{"batches":9}""");
        File.WriteAllText(Path.Combine(dir, "qphase", "plan-c.json"), """{"batches":1}""");
        File.WriteAllText(Path.Combine(dir, "qphase", "deeper", "x.json"), "{}");

        var second = MigrateV2Data.Apply(dir);

        second.Counts.Stores.Should().Be(1);
        Directory.Exists(Path.Combine(dir, "qphase")).Should().BeFalse("the source is merged away");
        File.ReadAllBytes(Path.Combine(archive, "qphase", "plan-a.json")).Should().Equal(qphase.Single(q => q.Name == "plan-a.json").Bytes, "an archived file is never overwritten");
        File.ReadAllText(Path.Combine(archive, "qphase", $"plan-a.v1-{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("""{"batches":9}"""u8))[..8]}.json")).Should().Be("""{"batches":9}""", "the colliding file got the content-hash suffix (SPEC §5.3)");
        File.ReadAllText(Path.Combine(archive, "qphase", "plan-c.json")).Should().Be("""{"batches":1}""");
        File.Exists(Path.Combine(archive, "qphase", "deeper", "x.json")).Should().BeTrue();
    }

    // ---------- scenario 18 ----------

    [Theory]
    [InlineData("0.0.0", null, false)]
    [InlineData("0.89.2", null, false)]
    [InlineData("1.9.9", null, false)]
    [InlineData("2.0.0", null, true)]
    [InlineData("2.3.1", "some-other-hash", true)]
    public void S18_TheAutomaticRunIsGatedByVersion_AndByTheMarkersTables(string version, string? marker, bool runs)
    {
        MigrationV2.ShouldRunAutomatically(Version.Parse(version), marker, MigrationV2Tables.Default.Hash).Should().Be(runs);
    }

    [Fact]
    public void S18_AMarkerOfTheseTables_StopsTheAutomaticRun_AndOneOfOtherTablesLetsItRunAgain()
    {
        var hash = MigrationV2Tables.Default.Hash;
        MigrationV2.ShouldRunAutomatically(new Version(2, 0, 0), hash, hash).Should().BeFalse("migrated by these tables already");

        var dir = Folder(mixed: false);
        MigrateV2Data.Plant("f3", "plan-pending", dir);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var said = new List<string>();

        MigrateV2Mode.RunAutomatic(dir, new Version(0, 89, 2), said.Add, MigrateV2Data.Sink(dir));
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeTrue("a v1 build never migrates by itself");
        Directory.Exists(MigrationV2.MigrationsDir(dir)).Should().BeFalse();

        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), said.Add, MigrateV2Data.Sink(dir));
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse("a v2 build migrates before it serves");
        MigrationV2.MarkerHash(dir).Should().Be(hash);

        // A second held session appears (a v1 server still alive wrote it) — the marker keeps the automatic run away from it …
        MigrateV2Data.Plant("f3", "plan-pending", dir);
        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), said.Add, MigrateV2Data.Sink(dir));
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeTrue("the marker gates the automatic run; only the one-shot mode scans again");

        // … until the tables of this build are not the marker's.
        File.WriteAllText(MigrationV2.MarkerFile(dir), """{"schema":1,"tablesHash":"an-older-build","finishedUtc":"2026-01-01T00:00:00Z","version":"2.0.0"}""");
        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), said.Add, MigrateV2Data.Sink(dir));
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse("a marker made by other tables runs the migration again");
        MigrationV2.MarkerHash(dir).Should().Be(hash);
    }

    [Fact]
    public void S18_TheTablesHash_ChangesWithEveryRow()
    {
        var shipped = MigrationV2Tables.Default;

        new[]
        {
            shipped with { RemovedSettings = [.. shipped.RemovedSettings, "COAI_X"] },
            shipped with { RenamedSettings = [("COAI_A", "COAI_B")] },
            shipped with { Stores = [.. shipped.Stores, "somewhere"] },
        }.Select(t => t.Hash).Append(shipped.Hash).Should().OnlyHaveUniqueItems();
        shipped.Hash.Should().Be(MigrationV2Tables.Default.Hash, "the same rows are the same hash");
    }

    [Fact]
    public void TheMarker_HoldsSchemaTablesHashFinishTimeAndVersion()
    {
        var dir = Folder();

        MigrateV2Data.Migration(dir, version: "2.0.0").Apply();

        using var marker = JsonDocument.Parse(File.ReadAllText(MigrationV2.MarkerFile(dir)));
        marker.RootElement.GetProperty("schema").GetInt32().Should().Be(1);
        marker.RootElement.GetProperty("tablesHash").GetString().Should().Be(MigrationV2Tables.Default.Hash);
        marker.RootElement.GetProperty("version").GetString().Should().Be("2.0.0");
        DateTime.Parse(marker.RootElement.GetProperty("finishedUtc").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind)
            .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }
}
