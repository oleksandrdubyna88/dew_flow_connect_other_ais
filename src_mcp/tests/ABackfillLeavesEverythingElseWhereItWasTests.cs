using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Findings;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E7.S3 — the narrow write: ONE round's authors and size into the session file's snapshot and the database projection, in the order the plan
/// fixes, without touching another byte of either, and so that running it again changes nothing. Every store here is a temp copy of E1's fixtures
/// recorded through the live writers; no live store is ever named.
/// </summary>
public sealed class ABackfillLeavesEverythingElseWhereItWasTests
{
    private const string Fixture = "resumed-agent";

    private static async Task<(BackfillWorld World, BackfillCounts Counts)> BackfilledAsync(string fixture = Fixture)
    {
        var world = BackfillWorld.Of(fixture);
        world.RecordLive();
        world.StripAuthors();

        return (world, await BackfillRig.RunOnceAsync(world));
    }

    private static string File0(BackfillWorld world) => Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Single();

    private static Finding Found(string title) => new(Severity.Major, Category.Reliability, "app.cs", 10, title, "because", "fix it", ["codex"]);

    /// <summary>The decisions a live round leaves: three findings, an accept, a reject WITH its reason, and the rest the round recorded.</summary>
    private static void RecordDecisions(BackfillWorld world)
    {
        var gate = world.Fixture.Rounds.GateSessions[0];
        var file = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!;
        var round = file.Rounds[0] with
        {
            ReviewerStates = [new ReviewerState("codex", "reviewer", "done", 3, "ok", 12.5, "gpt-6", "high")],
            CostUsd = 0.42,
        };
        var findings = new[] { Found("first"), Found("second"), Found("third") };
        using var db = RoundsDb.Open(world.DataDir, Serilog.Core.Logger.None)!;
        db.RecordRound(world.StateOf(gate), round, findings, new RoundContext("the scope", "abc1234", world.Caller, AgentLog: "{}") { BaseRef = "def5678" });
        db.RecordDecisions(file.State.SessionId, round.Stage, round.Number,
        [
            Decisions.Accept(findings, 0),
            Decisions.Reject(findings, 1, "the branch is behind main; verified absent from all three refs"),
        ]);
    }

    // ---------- the plausible wrong shape, kept as documentation ----------

    [Fact]
    public void ThroughRecordRound_TheResolutionsWouldBeLost_AndThatIsThePoint()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        RecordDecisions(world);
        StoreDump.Rows(world.DataDir, "SELECT * FROM findings").Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3, "positive control: three findings with their resolutions");
        var gate = world.Fixture.Rounds.GateSessions[0];
        var round = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!.Rounds[0];

        using (var db = RoundsDb.Open(world.DataDir, Serilog.Core.Logger.None)!)
        {
            db.RecordRound(world.StateOf(gate), round, []);
        }

        StoreDump.Rows(world.DataDir, "SELECT * FROM findings").Should().BeEmpty("a writer shaped like RecordRound with an empty findings list erases every finding and its resolution");
    }

    // ---------- decisions, versions, repeats ----------

    [Fact]
    public async Task ABackfill_LeavesEveryDecisionWhereItWas()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        RecordDecisions(world);
        world.StripAuthors();
        var before = StoreDump.EverythingElse(world.DataDir);
        before.Should().Contain("the branch is behind main").And.Contain("gpt-6").And.Contain("abc1234").And.Contain("def5678");

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written.Total.Should().BeGreaterThan(0, "positive control: the run wrote the authors");
        StoreDump.EverythingElse(world.DataDir).Should().Be(before, "findings, resolutions, reviewers, sessions and every non-authors column are byte-identical");
    }

    [Fact]
    public async Task ASecondWrite_ChangesNoRow()
    {
        var (world, first) = await BackfilledAsync();
        using (world)
        {
            var dump = StoreDump.AuthorsAndSize(world.DataDir);
            var file = File.ReadAllText(File0(world));
            dump.Should().Contain("authors|", "positive control: rows were written");

            var second = await BackfillRig.RunOnceAsync(world);

            first.Written.Total.Should().BeGreaterThan(0);
            second.Written.Total.Should().Be(0);
            second.Healed.Should().Be(0);
            StoreDump.AuthorsAndSize(world.DataDir).Should().Be(dump, "the multisets of round_authors and round_changes and every column are identical");
            File.ReadAllText(File0(world)).Should().Be(file);
        }
    }

    [Fact]
    public async Task AWriteRepeatedOnOneRound_ChangesNoRow()
    {
        var (world, _) = await BackfilledAsync();
        using (world)
        {
            var before = StoreDump.AuthorsAndSize(world.DataDir);
            using var db = RoundsDb.OpenExisting(world.DataDir, Serilog.Core.Logger.None)!;
            using var read = AuthorPeers.OpenReadOnly(world.DataDir)!;
            var writer = new BackfillWriter(world.DataDir, world.Store, db, TimeSpan.FromMilliseconds(300));
            var gate = new StoredHistory(read, world.Store).Gates().Single();
            var round = gate.Rounds.Single();

            for (var again = 0; again < 2; again++)
            {
                var write = new RoundWrite(gate, round, WriteHalves.Both, gate.Journal[0].Authors, null, null, RoundFingerprint.Read(read.CreateCommand, round.RoundId));
                writer.Write(write).Should().Be(WriteResult.Written);
            }

            StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before, "the same round written twice leaves the round_authors and round_changes multisets and every count identical");
        }
    }

    [Fact]
    public async Task TheTwoVersionsAreOnEveryWrittenRound()
    {
        var (world, _) = await BackfilledAsync();
        using (world)
        {
            StoreDump.Rows(world.DataDir, "SELECT authors_reader, authors_attribution FROM rounds").Should().Be($"{ObservedSession.ReaderVersion}|{RoundAttribution.Version}\n");
            var gate = world.Fixture.Rounds.GateSessions[0];
            var snapshot = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!.Rounds[0].Authors!;
            snapshot.Reader.Should().Be(ObservedSession.ReaderVersion);
            snapshot.Attribution.Should().Be(RoundAttribution.Version);
        }
    }

    // ---------- newer and stronger rows survive ----------

    [Fact]
    public async Task ANewerRow_Survives()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        Set(world, $"UPDATE rounds SET authors_reader = {ObservedSession.ReaderVersion + 1}");
        var gate = world.Fixture.Rounds.GateSessions[0];
        var file = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!;
        world.Store.Save(file with { Rounds = [.. file.Rounds.Select(round => round with { Authors = round.Authors! with { Reader = ObservedSession.ReaderVersion + 1 } })] });
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written.Total.Should().Be(0);
        counts.Kept[BackfillWhy.EqualOrNewer].Should().Be(1);
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
    }

    [Fact]
    public async Task ANewerReadersRow_IsNeverOverwrittenByAnOlderSnapshotOfTheSameStrength()
    {
        // the file still holds the current reader's snapshot while the database holds a newer reader's row: the projections disagree, but the older output never replaces the newer
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        Set(world, $"UPDATE rounds SET authors_reader = {ObservedSession.ReaderVersion + 1}");
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written.Total.Should().Be(0);
        counts.Kept[BackfillWhy.StrongerExisting].Should().Be(1);
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
    }

    [Fact]
    public async Task AStrongerOlderObservation_Survives_AndTheFileIsHealedFromIt()
    {
        // missing-child-transcripts reads `partial`; the stored row claims `observed` at older versions, and its snapshot was stripped
        using var world = BackfillWorld.Of("missing-child-transcripts");
        world.RecordLive();
        Set(world, "UPDATE rounds SET authors_state = 'observed', authors_reader = 0, authors_attribution = 0");
        StripSnapshots(world);
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written.Total.Should().Be(0, "a weaker result never replaces a stronger one");
        counts.Kept[BackfillWhy.StrongerExisting].Should().Be(1);
        counts.Healed.Should().Be(1, "the file is healed from the database instead of being left empty");
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before, "the database is untouched");
        Snapshot(world).Should().NotBeNull().And.Match<AuthorsSnapshot>(s => s.IdSource == "healed");
    }

    // ---------- a heal writes the file only ----------

    [Fact]
    public async Task AHeal_WritesTheFileOnly()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        var live = world.Live.Values.Single();
        StripSnapshots(world);
        RemoveTranscript(world);
        var before = StoreDump.Hash(StoreDump.AuthorsAndSize(world.DataDir) + StoreDump.EverythingElse(world.DataDir));

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Healed.Should().Be(1);
        counts.Written.Total.Should().Be(0);
        StoreDump.Hash(StoreDump.AuthorsAndSize(world.DataDir) + StoreDump.EverythingElse(world.DataDir)).Should().Be(before, "the database bytes are unchanged");
        var healed = Snapshot(world)!;
        healed.IdSource.Should().Be("healed");
        healed.Elsewhere.Should().Be(ActivityTotals.Unknown);
        healed.UnobservedLaunches.Should().Be(-1);
        healed.Rows.Select(row => (row.Agent, row.Model, row.Association)).Should().Equal(live.Rows.Select(row => (row.Agent, row.Model, row.Association)));
        StoreDump.Rows(world.DataDir, "SELECT authors_id_source FROM rounds").Should().Be($"{live.IdSource}\n", "the column still reads the live source");
    }

    // ---------- equal results write nothing ----------

    [Fact]
    public async Task AnEqualResult_WritesNothing()
    {
        var (world, _) = await BackfilledAsync();
        using (world)
        {
            var stamp = File.GetLastWriteTimeUtc(File0(world));
            await Task.Delay(30);
            using var rig = new BackfillRig(world);

            await rig.RunAsync();

            rig.Writes.Writes.Should().Be(0, "no claim, no transaction, no save");
            rig.Writes.Heals.Should().Be(0);
            rig.Counts.Kept[BackfillWhy.EqualOrNewer].Should().Be(1);
            File.GetLastWriteTimeUtc(File0(world)).Should().Be(stamp);
        }
    }

    [Fact]
    public async Task AnUnobservedRowAtCurrentVersions_IsComputedAgainAndWritesNothingWhenItIsTheSame()
    {
        var (world, _) = await BackfilledAsync("persistence-off");
        using (world)
        {
            StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().StartWith("unobserved: transcript not found");
            var before = StoreDump.AuthorsAndSize(world.DataDir);
            using var rig = new BackfillRig(world);

            await rig.RunAsync();

            rig.Counts.Kept[BackfillWhy.Unchanged].Should().Be(1, "rule 5 never keeps an unobserved row, so its equal result is what keeps the run idempotent");
            rig.Writes.Writes.Should().Be(0);
            StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
        }
    }

    // ---------- a live server writes in between ----------

    [Fact]
    public async Task ARoundChangedUnderTheRun_ByItsState_IsDeferred_AndNothingIsWritten()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        var file = File.ReadAllBytes(File0(world));
        var stamp = File.GetLastWriteTimeUtc(File0(world));
        using var rig = new BackfillRig(world, beforeWrite: _ => Set(world, "UPDATE rounds SET authors_state = 'partial: read budget', authors_reader = 1, authors_attribution = 1"));

        await rig.RunAsync();

        rig.Counts.Deferred[BackfillWhy.ChangedUnderUs].Should().Be(1);
        rig.Counts.Written.Total.Should().Be(0);
        File.ReadAllBytes(File0(world)).Should().Equal(file, "the file was not saved");
        File.GetLastWriteTimeUtc(File0(world)).Should().Be(stamp);
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().Be("partial: read budget\n", "the live server's row stands after a restart");
        StoreDump.Rows(world.DataDir, "SELECT COUNT(*) FROM round_authors").Should().Be("0\n");
    }

    [Fact]
    public async Task ARoundChangedUnderTheRun_OnlyInItsAuthorsRows_IsDeferredToo()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        var file = File.ReadAllBytes(File0(world));
        Set(world, $"UPDATE rounds SET authors_reader = {ObservedSession.ReaderVersion - 1}");
        using var rig = new BackfillRig(world, beforeWrite: _ => Set(world, "INSERT INTO round_authors (round_id, ordinal, who) VALUES (1, 99, 'main')"));

        await rig.RunAsync();

        rig.Counts.Deferred[BackfillWhy.ChangedUnderUs].Should().Be(1, "four unchanged columns do not prove unchanged rows");
        File.ReadAllBytes(File0(world)).Should().NotBeEmpty();
        file.Should().NotBeEmpty();
        StoreDump.Rows(world.DataDir, "SELECT COUNT(*) FROM round_authors WHERE ordinal = 99").Should().Be("1\n", "the live server's row is what both stores hold");
    }

    /// <summary>
    /// Code gate findings 0, 6 and 18 (vibe, codex, kimi): the fingerprint covers EVERY column of the round and of its rows, not a hand-kept list — a
    /// live server that re-recorded the round's head would otherwise pass the check, and the size measured from the OLD commits would be written.
    /// </summary>
    [Fact]
    public async Task ARoundChangedUnderTheRun_InAColumnTheBackfillDoesNotWrite_IsDeferredToo()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        using var rig = new BackfillRig(world, beforeWrite: _ => Set(world, "UPDATE rounds SET head_sha = 'ffffffffffffffffffffffffffffffffffffffff'"));

        await rig.RunAsync();

        rig.Counts.Deferred[BackfillWhy.ChangedUnderUs].Should().Be(1, "ANY write to the round since its selection defers it: a re-recorded head makes a size from the old commits stale");
        rig.Counts.Written.Total.Should().Be(0);
    }

    /// <summary>
    /// Code gate finding 18 (kimi): the projection's column list, the heal's and the writer's must agree, and nothing but a test forces it. This pins it:
    /// the projection reads every column of <c>round_authors</c> the writer fills (the table's own list, less its two keys), and a healed snapshot
    /// projects to exactly the rows it was built from.
    /// </summary>
    [Fact]
    public async Task TheProjectionAndTheHeal_ReadEveryColumnTheWriterWrites()
    {
        var (world, _) = await BackfilledAsync();
        using var disposing = world;
        SqliteConnection.ClearAllPools();
        using var db = AuthorPeers.OpenReadOnly(world.DataDir)!;
        var round = new StoredHistory(db, world.Store).Gates()[0].Rounds[0];
        var columns = StoreDump.Rows(world.DataDir, "SELECT name FROM pragma_table_info('round_authors') WHERE name NOT IN ('round_id', 'ordinal')")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var whole = StoreDump.Rows(world.DataDir, $"SELECT {string.Join(", ", columns)} FROM round_authors WHERE round_id = {round.RoundId} ORDER BY ordinal")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        whole.Should().NotBeEmpty("the backfilled round has rows to compare");
        AuthorsProjection.Read(db, round.RoundId).Skip(1).Should().Equal(whole,
            "a column added to round_authors that the projection does not compare would hide a live rewrite of it from the equality — and the writer's delete-then-insert would erase it");
        AuthorsProjection.Agree(AuthorsHeal.From(db.CreateCommand, round)!, db, round.RoundId).Should().BeTrue("a healed snapshot projects to exactly the rows it was rebuilt from");
    }

    [Fact]
    public async Task TheTransactionIsImmediate_ABusyWriterDefersTheRoundAtBegin_NotAtTheFirstUpdate()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        var file = File.ReadAllBytes(File0(world));
        using var rig = new BackfillRig(world, busyWait: TimeSpan.FromMilliseconds(200));
        SqliteConnection.ClearAllPools();
        using var holder = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False");
        holder.Open();
        BackfillWorld.Run(holder, "BEGIN IMMEDIATE");

        await rig.RunAsync();

        rig.Counts.Deferred[BackfillWhy.Busy].Should().Be(1);
        File.ReadAllBytes(File0(world)).Should().Equal(file, "busy at BEGIN: the file was never saved — a deferred transaction would have saved it and failed at the first UPDATE");
        BackfillWorld.Run(holder, "ROLLBACK");
    }

    [Fact]
    public async Task ABusySession_IsDeferred_AndNeitherSideIsWritten()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        var file = File.ReadAllBytes(File0(world));
        var gate = world.Fixture.Rounds.GateSessions[0];
        using var claim = SessionClaim.TryTake(world.DataDir, world.RepoOf(gate), BackfillWorld.BranchOf(gate));
        claim.Should().NotBeNull("the test holds the session's claim, as a live round would");
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Deferred[BackfillWhy.Busy].Should().Be(1);
        counts.Written.Total.Should().Be(0);
        File.ReadAllBytes(File0(world)).Should().Equal(file);
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
    }

    // ---------- crashes ----------

    [Fact]
    public async Task TheSnapshotIsSavedBeforeTheProjection_AndTheNextRunReprojectsItWithoutTheTranscript()
    {
        // the uninterrupted run, for the byte-for-byte comparison
        var (clean, _) = await BackfilledAsync();
        string expected;
        using (clean)
        {
            expected = StoreDump.AuthorsAndSize(clean.DataDir);
        }

        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        using (var killed = new BackfillRig(world, fault: point =>
        {
            if (point == WritePoint.FileSaved)
            {
                throw new IOException("the run is killed between its two writes");
            }
        }))
        {
            var run = () => killed.RunAsync();
            await run.Should().ThrowAsync<IOException>();
        }

        Snapshot(world).Should().NotBeNull("the file holds the snapshot");
        StoreDump.Rows(world.DataDir, "SELECT COUNT(*) FROM round_authors").Should().Be("0\n");
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().Be("\n", "the database holds no authors for the round");

        RemoveTranscript(world);
        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written[WrittenAs.Reprojected].Should().Be(1, "re-projected from the file; the transcript is gone, so it could not have been read");
        StripId(StoreDump.AuthorsAndSize(world.DataDir)).Should().Be(StripId(expected), "the rows and columns equal those of an uninterrupted run");
    }

    /// <summary>The id source legitimately differs between two worlds (live <c>environment</c> in both here); the dump is compared as it is.</summary>
    private static string StripId(string dump) => dump;

    [Fact]
    public async Task TheCrashShape_IsReprojectedNotKept()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        Set(world, "UPDATE rounds SET authors_state = 'partial: read budget'");
        StripSnapshots(world);
        using (var killed = new BackfillRig(world, fault: point =>
        {
            if (point == WritePoint.FileSaved)
            {
                throw new IOException("killed");
            }
        }))
        {
            var run = () => killed.RunAsync();
            await run.Should().ThrowAsync<IOException>("the recompute (observed) is saved to the file before the database half");
        }

        Snapshot(world)!.Render().Should().Be("observed");
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().Be("partial: read budget\n");

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written[WrittenAs.Reprojected].Should().Be(1, "a current observed snapshot over a current partial row is re-projected, never kept");
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().Be("observed\n");
        (await BackfillRig.RunOnceAsync(world)).Written.Total.Should().Be(0);
    }

    // ---------- no file ----------

    [Fact]
    public async Task ARoundWithoutItsFile_IsWrittenToTheDatabaseOnly_AndASecondRunKeepsIt()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        File.Delete(File0(world));

        var first = await BackfillRig.RunOnceAsync(world);

        first.Written[WrittenAs.DatabaseOnly].Should().Be(1);
        Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Should().BeEmpty("no file is made for a round whose file is gone");
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().NotBe("\n");
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var second = await BackfillRig.RunOnceAsync(world);

        second.Written.Total.Should().Be(0);
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
    }

    // ---------- the file keeps everything else ----------

    [Fact]
    public async Task TheSessionFileKeepsEveryOtherField()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        var before = Normalised(File.ReadAllText(File0(world)));

        await BackfillRig.RunOnceAsync(world);

        var after = Normalised(File.ReadAllText(File0(world)));
        after.Should().NotBe(before, "positive control: the snapshot was added");
        Without(after).Should().Be(Without(before), "the file before and after, with the touched round's authors removed from both, is equal");
    }

    private static JsonElement Normalised(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Without(JsonElement session)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(session.GetRawText())!;
        foreach (var round in node["rounds"]!.AsArray())
        {
            round!.AsObject().Remove("authors");
            round.AsObject().Remove("size");
            round.AsObject().Remove("gitlinks");
        }

        return node.ToJsonString();
    }

    // ---------- an older server strips the snapshot after a verified write ----------

    [Fact]
    public async Task ASnapshotLostToAConcurrentOpen_IsRecomputedOnTheNextRun_WhileTheTranscriptExists()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        var gate = world.Fixture.Rounds.GateSessions[0];
        var copyBeforeTheWrite = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!;
        await BackfillRig.RunOnceAsync(world);
        var first = Snapshot(world)!;
        var idSource = StoreDump.Rows(world.DataDir, "SELECT authors_id_source FROM rounds");
        world.Store.Save(copyBeforeTheWrite);
        Snapshot(world).Should().BeNull("open saved a copy it loaded before the write");

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Written[WrittenAs.RecomputedSnapshotLost].Should().Be(1);
        AuthorsProjection.Of(Snapshot(world)!).Should().Equal(AuthorsProjection.Of(first), "the placed ids and rows equal the first write's");
        StoreDump.Rows(world.DataDir, "SELECT authors_id_source FROM rounds").Should().Be(idSource, "the column keeps the live source");
    }

    [Fact]
    public async Task ASnapshotLostToAConcurrentOpen_IsHealedOnTheNextRun_WhenTheTranscriptIsGone()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        world.StripAuthors();
        var gate = world.Fixture.Rounds.GateSessions[0];
        var copyBeforeTheWrite = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!;
        await BackfillRig.RunOnceAsync(world);
        world.Store.Save(copyBeforeTheWrite);
        RemoveTranscript(world);
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var counts = await BackfillRig.RunOnceAsync(world);

        counts.Healed.Should().Be(1);
        Snapshot(world)!.IdSource.Should().Be("healed");
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before, "the database bytes are unchanged");
    }

    // ---------- E5's null rule ----------

    [Fact]
    public async Task ALiveReRecordWithoutTheSnapshot_KeepsTheBackfilledAuthors()
    {
        var (world, _) = await BackfilledAsync();
        using (world)
        {
            var before = StoreDump.AuthorsAndSize(world.DataDir);
            var gate = world.Fixture.Rounds.GateSessions[0];
            var round = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!.Rounds[0] with { Authors = null };

            using (var db = RoundsDb.Open(world.DataDir, Serilog.Core.Logger.None)!)
            {
                db.RecordRound(world.StateOf(gate), round, []);
            }

            StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before, "null means not recorded, never recorded empty");
        }
    }

    // ---------- text-free ----------

    [Fact]
    public async Task NoSentinelReachesTheDatabaseOrTheSessionFile()
    {
        var (world, _) = await BackfilledAsync();
        using (world)
        {
            SqliteConnection.ClearAllPools();
            using (var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False"))
            {
                db.Open();
                BackfillWorld.Run(db, "PRAGMA wal_checkpoint(TRUNCATE)");
            }

            SqliteConnection.ClearAllPools();
            var bytes = Directory.GetFiles(world.DataDir, RoundsDb.FileName + "*").SelectMany(File.ReadAllBytes).ToArray();
            var sentinel = System.Text.Encoding.UTF8.GetBytes(FixtureManifest.Sentinel);
            bytes.AsSpan().IndexOf(sentinel).Should().Be(-1, "the database");
            File.ReadAllText(File0(world)).Should().NotContain(FixtureManifest.Sentinel);
            Snapshot(world)!.Rows.Should().NotBeEmpty("positive control: the fixture was read");
        }
    }

    // ---------- helpers ----------

    private static AuthorsSnapshot? Snapshot(BackfillWorld world)
    {
        var gate = world.Fixture.Rounds.GateSessions[0];

        return world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!.Rounds[0].Authors;
    }

    private static void Set(BackfillWorld world, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        BackfillWorld.Run(db, sql);
    }

    private static void StripSnapshots(BackfillWorld world)
    {
        foreach (var gate in world.Fixture.Rounds.GateSessions)
        {
            if (world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate)) is { } file)
            {
                world.Store.Save(file with { Rounds = [.. file.Rounds.Select(round => round with { Authors = null })] });
            }
        }
    }

    private static void RemoveTranscript(BackfillWorld world)
    {
        Directory.Delete(world.Fixture.Home.ProjectsDir, recursive: true);
        Directory.CreateDirectory(world.Fixture.Home.ProjectsDir);
    }
}
