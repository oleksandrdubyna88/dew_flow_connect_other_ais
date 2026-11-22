using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the epic plan gate of E1 added to the v2 migration (<c>v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/SPEC.md</c> §5 and §6): convert on load, a restart-safe store merge
/// with path identity, a durable notice for every answered hold, the sweep of late cards at every v2 start, and a journal whose torn tail is cut back before the next line.
/// </summary>
/// <remarks>
/// Each test names the section it holds. The sections that change an existing scenario (the unreadable object, scenario 13, and the merge suffix, scenario 17) are
/// rewritten in <see cref="MigrateV2RunTests"/> where those scenarios live.
/// </remarks>
public sealed class MigrateV2GateTests : IDisposable
{
    private const string AnsweredCard = "20ef85d344a0";

    private const string AnsweredSession = "6991f13e";

    private readonly TempDir _root = TempDir.For("coai-migrate-v2-");

    private int _folders;

    public void Dispose() => _root.Dispose();

    private string Folder()
    {
        var dir = Path.Combine(_root.Path, "data" + _folders++);
        Directory.CreateDirectory(dir);

        return dir;
    }

    private static string Sha8(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];

    private static string[] JournalLines(string dir) => File.ReadAllLines(MigrationV2.JournalFile(dir)).Where(l => l.Length > 0).ToArray();

    // ---------- §5.1 convert on load ----------

    /// <summary>A folder at "a v2 server has migrated it": a marker, and nothing else; then a held plan session a v1 process writes.</summary>
    private (string Dir, string File, SessionState State) MigratedFolderWithALateHold()
    {
        var dir = Folder();
        MigrateV2Data.Apply(dir).Status.Should().Be("done");
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeTrue();
        MigrateV2Data.Plant("f3", "plan-pending", dir, cards: false);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var state = MigrateV2Data.Load(dir, file).State;
        LegacyHold.Stands(state).Should().BeTrue("the fixture is a hold a v1 process wrote after the marker");

        return (dir, file, state);
    }

    [Fact]
    public void S5_1_AHeldSessionAV1ProcessWroteAfterTheMarker_IsConvertedByTheLoad_Once_AndJournaledOnLoad()
    {
        var (dir, file, state) = MigratedFolderWithALateHold();
        var reference = Folder();
        MigrateV2Data.Plant("f3", "plan-pending", reference, cards: false);
        MigrateV2Data.Apply(reference);
        var converted = File.ReadAllBytes(MigrateV2Data.SessionFile(reference, "plan"));

        var loaded = new SessionStore(dir).Load(MigrateV2Data.Repo, state.Branch, state.Document, state.Feature)!;

        LegacyHold.Stands(loaded.State).Should().BeFalse("the load leaves the hold");
        loaded.State.AwaitingResolve.Should().BeTrue();
        loaded.State.AdvanceOnResolve.Should().BeTrue();
        loaded.Rounds[^1].Note.Should().Be(MigrationV2.ConversionNote);
        File.ReadAllBytes(file).Should().Equal(converted, "saved to the same path, the very bytes the migration writes");
        var lines = MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Where(l => l.Detail == "on-load").ToList();
        lines.Should().ContainSingle().Which.Should().Match<MigrationJournalLine>(l => l.Kind == "session" && l.State == "done" && l.Id == Path.GetFileName(file));

        new SessionStore(dir).Load(MigrateV2Data.Repo, state.Branch, state.Document, state.Feature);
        File.ReadAllBytes(file).Should().Equal(converted, "a second load changes nothing");
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Count(l => l.Detail == "on-load").Should().Be(1, "converted once");
    }

    [Fact]
    public void S5_1_ALoadThatCannotTakeTheClaim_ReturnsTheConvertedSession_AndWritesNothing()
    {
        var (dir, file, state) = MigratedFolderWithALateHold();
        var bytes = File.ReadAllBytes(file);
        var journalFile = MigrationV2.JournalFile(dir);
        var journal = File.Exists(journalFile) ? File.ReadAllBytes(journalFile) : [];
        using var claim = SessionClaim.TryTake(dir, MigrateV2Data.Repo, state.Branch);
        claim.Should().NotBeNull("this test is the call that holds the session");

        var loaded = new SessionStore(dir).Load(MigrateV2Data.Repo, state.Branch, state.Document, state.Feature)!;

        LegacyHold.Stands(loaded.State).Should().BeFalse("the caller is given the session as v2 reads it …");
        File.ReadAllBytes(file).Should().Equal(bytes, "… but only the holder of the claim writes the file");
        (File.Exists(journalFile) ? File.ReadAllBytes(journalFile) : []).Should().Equal(journal, "no journal line either: only the holder of the claim journals");
    }

    [Fact]
    public void S5_1_ASessionTheRunDeferred_IsConvertedByTheNextLoad_NotOnlyByTheNextRun()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-pending", dir, cards: false);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var state = MigrateV2Data.Load(dir, file).State;
        using (SessionClaim.TryTake(dir, MigrateV2Data.Repo, state.Branch))
        {
            MigrateV2Data.Apply(dir).Counts.Deferred.Should().Be(1);
        }

        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeTrue("the run deferred it and wrote no marker");

        new SessionStore(dir).Load(MigrateV2Data.Repo, state.Branch, state.Document, state.Feature);

        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse();
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Should().Contain(l => l.Detail == "on-load");
    }

    [Fact]
    public async Task S5_1_AClientRequestOnADeferredSession_ConvertsItOnce_AndTheEmptyResolveAdvancesOnce()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-resolved", dir, cards: false);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var before = MigrateV2Data.Load(dir, file);
        using (SessionClaim.TryTake(dir, MigrateV2Data.Repo, before.State.Branch))
        {
            MigrateV2Data.Apply(dir).Counts.Deferred.Should().Be(1);
        }

        var reply = await MigrateV2Data.Service(dir).ResolveAsync(MigrateV2Data.Repo, before.State.Branch, "[]", before.State.Document, before.State.Feature);

        JsonDocument.Parse(reply).RootElement.TryGetProperty("error", out var error).Should().BeFalse(reply + error);
        var after = MigrateV2Data.Load(dir, file);
        LegacyHold.Stands(after.State).Should().BeFalse("the request that touched the session took it out of the hold");
        after.State.Stage.Should().Be(Stages.Of(before.State.Stage).AdvancesTo, "and the resolve advanced it once");
    }

    // ---------- §5.2 / §6.4 an unreadable object is an incomplete migration ----------

    [Fact]
    public void S5_2_AnUnreadableSession_ThatIsReadableAtALaterLoad_IsConvertedByTheLoad()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-pending", dir, cards: false);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var state = MigrateV2Data.Load(dir, file).State;
        var good = File.ReadAllBytes(file);
        File.WriteAllText(file, """{"state": {"sessionId": "torn""");
        MigrateV2Data.Apply(dir).Counts.Unreadable.Should().Be(1);
        File.WriteAllBytes(file, good); // somebody repairs it, or the disk finishes writing it

        new SessionStore(dir).Load(MigrateV2Data.Repo, state.Branch, state.Document, state.Feature);

        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse("a file that was unreadable and is readable later converts at the next load");
    }

    [Fact]
    public void S5_2_TheAutomaticRun_RetriesAnUnreadableObjectAtEveryStart_ButNeverLoopsInsideOne()
    {
        var dir = Folder();
        File.WriteAllText(Path.Combine(dir, "settings.json"), "this is not json");
        var said = new List<string>();

        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), said.Add, MigrateV2Data.Sink(dir));

        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Count(l => l.State == "unreadable").Should().Be(1, "one start, one look at the object");
        said.Should().Contain(w => w.Contains("did not finish", StringComparison.Ordinal), "an incomplete migration is said, and the server starts anyway");
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse();

        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), said.Add, MigrateV2Data.Sink(dir));

        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Count(l => l.State == "unreadable").Should().Be(2, "the next start looks again");
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{"COAI_ON_EXHAUSTED":"human"}""");
        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), said.Add, MigrateV2Data.Sink(dir));
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeTrue("once the object is readable the next start completes");
    }

    // ---------- §5.3 / §6.1 the store merge ----------

    /// <summary>An archive that already holds <c>qphase/</c> files, and a re-created <c>qphase/</c> beside it.</summary>
    private string FolderWithAMerge(out Dictionary<string, string> source)
    {
        var dir = Folder();
        var archive = Path.Combine(dir, "migrations", "v1-archive", "qphase");
        Directory.CreateDirectory(Path.Combine(archive, "deeper"));
        File.WriteAllText(Path.Combine(archive, "a.json"), "A-archived");
        File.WriteAllText(Path.Combine(archive, "b.json"), "B-same");
        File.WriteAllText(Path.Combine(archive, "deeper", "d.json"), "D-archived");
        source = new Dictionary<string, string>
        {
            ["a.json"] = "A-recreated", // a DIFFERENT file at an occupied path
            ["b.json"] = "B-same", // the SAME bytes at the same path
            ["c.json"] = "C-new",
            ["deeper/d.json"] = "D-recreated",
            ["deeper/e.json"] = "E-new",
        };
        foreach (var (name, text) in source)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, "qphase", name))!);
            File.WriteAllText(Path.Combine(dir, "qphase", name), text);
        }

        return dir;
    }

    [Fact]
    public void S5_3_ADifferentFileAtAnOccupiedPath_GetsTheHashSuffix_AndTheSameBytesAtTheSamePath_AreRemovedNotCopied()
    {
        var dir = FolderWithAMerge(out _);
        var archive = Path.Combine(dir, "migrations", "v1-archive", "qphase");

        var report = MigrateV2Data.Apply(dir);

        report.Status.Should().Be("done");
        Directory.Exists(Path.Combine(dir, "qphase")).Should().BeFalse("the source is merged away");
        File.ReadAllText(Path.Combine(archive, "a.json")).Should().Be("A-archived", "an archived file is never overwritten");
        File.ReadAllText(Path.Combine(archive, $"a.v1-{Sha8("A-recreated")}.json")).Should().Be("A-recreated", "the suffix is the first eight hex digits of the SHA-256 of the source's content");
        File.ReadAllText(Path.Combine(archive, "deeper", $"d.v1-{Sha8("D-recreated")}.json")).Should().Be("D-recreated", "a nested file collides by its relative path");
        File.ReadAllText(Path.Combine(archive, "c.json")).Should().Be("C-new");
        File.ReadAllText(Path.Combine(archive, "deeper", "e.json")).Should().Be("E-new");
        Directory.GetFiles(archive, "*", SearchOption.AllDirectories).Should().HaveCount(7, "three archived, two new, two suffixed — and the same bytes at the same path made no copy");
    }

    [Fact]
    public void S5_3_AMergeRepeatedWithTheSameSource_AddsNothing()
    {
        var dir = FolderWithAMerge(out var source);
        MigrateV2Data.Apply(dir);
        var after = MigrateV2Data.Tree(dir, withFolders: true);
        foreach (var (name, text) in source)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, "qphase", name))!);
            File.WriteAllText(Path.Combine(dir, "qphase", name), text);
        }

        MigrateV2Data.Apply(dir).Status.Should().Be("done");

        MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(after, "every file is either at its own path with its own bytes or under its own hash: all of it is there already");
    }

    [Fact]
    public void S6_1_TwoNamesWithIdenticalBytes_AreBothKept_BecauseOnlyTheSamePathIsTheSameFile()
    {
        var dir = Folder();
        var archive = Path.Combine(dir, "migrations", "v1-archive", "qphase");
        Directory.CreateDirectory(archive);
        File.WriteAllText(Path.Combine(archive, "x.json"), "SAME");
        Directory.CreateDirectory(Path.Combine(dir, "qphase"));
        File.WriteAllText(Path.Combine(dir, "qphase", "x.json"), "SAME");
        File.WriteAllText(Path.Combine(dir, "qphase", "y.json"), "SAME");

        MigrateV2Data.Apply(dir).Status.Should().Be("done");

        File.ReadAllText(Path.Combine(archive, "x.json")).Should().Be("SAME");
        File.ReadAllText(Path.Combine(archive, "y.json")).Should().Be("SAME", "y.json is another file: it is kept although x.json has its bytes");
        Directory.GetFiles(archive).Should().HaveCount(2);
    }

    [Fact]
    public void S5_3_AKillAtEveryBoundaryOfAMerge_ThenOneCleanRun_EqualsOneCleanRun()
    {
        var reference = FolderWithAMerge(out _);
        var boundaries = 0;
        MigrateV2Data.Migration(reference, atBoundary: _ => boundaries++).Apply().Status.Should().Be("done");
        var expected = MigrateV2Data.Tree(reference, withFolders: true);
        boundaries.Should().BeGreaterThanOrEqualTo(5 + 5, "a boundary between every two file moves of the merge, as well as around the object");

        for (var k = 0; k < boundaries; k++)
        {
            var dir = FolderWithAMerge(out _);
            var killed = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (n == k) { throw new SimulatedKill(k); } }).Apply();
            killed.Should().Throw<SimulatedKill>($"boundary {k} of {boundaries}");

            MigrateV2Data.Apply(dir).Status.Should().Be("done", $"after a kill at boundary {k}");

            MigrateV2Data.Tree(dir, withFolders: true).Should().Equal(expected, $"a kill at boundary {k} between two file moves, then a clean run, equals one clean run — no duplicate, no loss");
        }
    }

    // ---------- §5.4 / §6.2 an answered hold reaches the person durably ----------

    private string FolderWithAnAnsweredHold()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-answered", dir);
        LegacyHold.Stands(MigrateV2Data.Load(dir, MigrateV2Data.SessionFile(dir, "plan")).State).Should().BeTrue();

        return dir;
    }

    [Fact]
    public void S5_4_AnAnsweredHold_GetsOneDurableNotice_KeyedByTheCardId()
    {
        var dir = FolderWithAnAnsweredHold();

        var report = MigrateV2Data.Apply(dir);

        report.Status.Should().Be("done");
        report.Notices.Should().ContainSingle().Which.Should().Be(new MigrateV2Notice("v2-converted-answered-hold", AnsweredCard, AnsweredSession, "fix"));
        var written = MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold");
        var notice = written.Should().ContainSingle("one entry per answered hold").Subject;
        notice.GetProperty("subject").GetString().Should().Be(AnsweredCard, "the key is the card id");
        notice.GetProperty("session").GetString().Should().Be(AnsweredSession);
        notice.GetProperty("answer").GetString().Should().Be("fix", "the decision the person made and v2 did not apply");
        notice.GetProperty("class").GetString().Should().Be("outcome");
        notice.TryGetProperty("run", out _).Should().BeTrue("written through the shared writer, which stamps the run");

        MigrateV2Data.Apply(dir);
        MigrateV2Data.Mode(dir, "--apply");
        MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold").Should().HaveCount(1, "written once");
    }

    [Fact]
    public void S5_4_ACodeInTheCatalog_IsWhatTheNoticeCarries()
    {
        ServerNoticeCodes.All.Should().Contain("v2-converted-answered-hold", "the code is in the catalog, so the redactor guard and the unique-codes guard see it");
    }

    [Fact]
    public void S5_4_AKillAtEveryBoundary_NeverWritesTwoNoticesForOneCard_AndNeverNone()
    {
        var reference = FolderWithAnAnsweredHold();
        var boundaries = 0;
        MigrateV2Data.Migration(reference, atBoundary: _ => boundaries++).Apply().Status.Should().Be("done");

        for (var k = 0; k < boundaries; k++)
        {
            var dir = FolderWithAnAnsweredHold();
            var killed = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (n == k) { throw new SimulatedKill(k); } }).Apply();
            killed.Should().Throw<SimulatedKill>();

            MigrateV2Data.Apply(dir).Status.Should().Be("done");

            MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold").Should().ContainSingle($"a kill at boundary {k}, then a clean run: the notice is written once, whichever side of the conversion the kill fell");
            LegacyHold.Stands(MigrateV2Data.Load(dir, MigrateV2Data.SessionFile(dir, "plan")).State).Should().BeFalse();
        }
    }

    [Fact]
    public void S5_4_ANoticeThatCannotBeWritten_LeavesTheSessionHeld_AndTheMarkerAway_SoNoAnswerIsConvertedUnannounced()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);
        var refusing = new MigrationV2(dir, _ => { }, _ => false);

        var report = refusing.Apply();

        report.Status.Should().Be("partial");
        report.Counts.Deferred.Should().Be(1);
        File.ReadAllBytes(file).Should().Equal(bytes, "the person's answer is not overridden until the person can be told");
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse();
        MigrateV2Data.Apply(dir).Status.Should().Be("done", "the next run, with a writer that works, announces and converts");
        MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold").Should().ContainSingle();
    }

    [Fact]
    public void S5_4_ANoticeTheWriterAcceptedButTheDiskNeverTook_LeavesTheSessionHeld_BecauseTheMigrationTrustsTheFileNotTheAnswer()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);
        var accepting = new MigrationV2(dir, _ => { }, _ => true); // the queue took it; the line never reached the file

        var report = accepting.Apply();

        report.Status.Should().Be("partial", "an accepted notice is not a written one: the line is read back");
        File.ReadAllBytes(file).Should().Equal(bytes);
        File.Exists(MigrationV2.MarkerFile(dir)).Should().BeFalse();
        MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold").Should().BeEmpty();
    }

    [Fact]
    public void S6_2_ADryRun_ListsTheNotice_AndWritesNoNoticeFile()
    {
        var dir = FolderWithAnAnsweredHold();
        var before = MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true);

        var plan = MigrateV2Data.Plan(dir);
        var (code, output, err) = MigrateV2Data.Mode(dir);

        code.Should().Be(0, err);
        plan.Notices.Should().ContainSingle().Which.Should().Be(new MigrateV2Notice("v2-converted-answered-hold", AnsweredCard, AnsweredSession, "fix"));
        var report = MigrateV2Data.Report(output);
        report.GetProperty("notices").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("subject").GetString().Should().Be(AnsweredCard);
        File.Exists(Path.Combine(dir, ServerNotices.Name)).Should().BeFalse("a dry run writes no notice");
        MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true).Should().Equal(before);
    }

    [Fact]
    public void S6_2_TheDryRunsNotices_AreTheNoticesAnApplyWrites_AndOnceWrittenAreNoLongerProposed()
    {
        var dir = FolderWithAnAnsweredHold();
        var plan = MigrateV2Data.Plan(dir);

        var applied = MigrateV2Data.Apply(dir);

        plan.Notices.Should().NotBeEmpty();
        applied.Notices.Should().BeEquivalentTo(plan.Notices);
        MigrateV2Data.Plan(dir).Notices.Should().BeEmpty("the session left its hold and the notice is in the file");
    }

    // ---------- one scan of the notice history per run, not one per answered hold ----------

    /// <summary>The held plan session with THREE answered cards: the fixture's own, and two copies under other ids (a card names its session, not its sibling).</summary>
    private string FolderWithThreeAnsweredHolds()
    {
        var dir = FolderWithAnAnsweredHold();
        var cards = Path.Combine(dir, "escalations");
        foreach (var id in new[] { "bbbbbbbbbbbb", "cccccccccccc" })
        {
            File.WriteAllText(Path.Combine(cards, id + ".json"), File.ReadAllText(Path.Combine(cards, AnsweredCard + ".json")).Replace(AnsweredCard, id));
            File.WriteAllText(Path.Combine(cards, id + ".answer.json"), File.ReadAllText(Path.Combine(cards, AnsweredCard + ".answer.json")).Replace(AnsweredCard, id));
        }

        return dir;
    }

    private static (MigrationV2 Migration, Func<int> Scans) CountingScans(string dir)
    {
        var scans = 0;
        var migration = new MigrationV2(dir, _ => { }, MigrateV2Data.Sink(dir))
        {
            NoticeLines = folder =>
            {
                scans++;

                return ServerNotices.Lines(folder);
            },
        };

        return (migration, () => scans);
    }

    [Fact]
    public void AnApplyOverThreeAnsweredHolds_ReadsTheNoticeHistoryOnce_AndConfirmsEachNoticeFromItsTailOnly()
    {
        var dir = FolderWithThreeAnsweredHolds();
        var (migration, scans) = CountingScans(dir);

        var report = migration.Apply();

        report.Status.Should().Be("done");
        MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold").Select(n => n.GetProperty("subject").GetString()).Should().BeEquivalentTo(AnsweredCard, "bbbbbbbbbbbb", "cccccccccccc");
        scans().Should().Be(1, "one read to learn what is there; the check before a write is answered by the set, and a written notice is confirmed from its tail (MigrateV2FinalRoundTests), not by another read of the history");
    }

    [Fact]
    public void AnApplyWhoseNoticesAreAllOnDisk_ReadsTheNoticeHistoryOnce_AndWritesNoSecondNotice()
    {
        var dir = FolderWithThreeAnsweredHolds();
        MigrateV2Data.Apply(dir).Status.Should().Be("done");
        MigrateV2Data.Plant("f3", "plan-answered", dir, cards: false); // the held session a late v1 process wrote again; the notices are already in the file
        var (migration, scans) = CountingScans(dir);

        migration.Apply();

        scans().Should().Be(1, "three answered holds, three subjects already in the set");
        MigrateV2Data.NoticesOf(dir, "v2-converted-answered-hold").Should().HaveCount(3, "no notice twice");
    }

    [Fact]
    public void ADryRunOverThreeAnsweredHolds_ReadsTheNoticeHistoryOnce()
    {
        var dir = FolderWithThreeAnsweredHolds();
        var (migration, scans) = CountingScans(dir);

        var plan = migration.Plan();

        plan.Notices.Should().HaveCount(3);
        scans().Should().Be(1);
    }

    [Fact]
    public void ANoticeThatTheDiskNeverTook_IsNotInTheSet_SoTheReadBackStillRefuses()
    {
        var dir = FolderWithThreeAnsweredHolds();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);
        var accepting = new MigrationV2(dir, _ => { }, _ => true) { NoticeLines = ServerNotices.Lines };

        var report = accepting.Apply();

        report.Status.Should().Be("partial");
        File.ReadAllBytes(file).Should().Equal(bytes, "the set is never fed by the answer of the sink, only by what a read of the file found");
    }

    // ---------- §6.3 late v1 cards ----------

    private static string WriteOpenCard(string dir, string id)
    {
        var card = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "migrate-v2", "f3", "plan-pending", "escalations"), "b7a7e49109a7.json").Single()))!.AsObject();
        card["id"] = id;
        card["sessionId"] = "late-session";
        Directory.CreateDirectory(Path.Combine(dir, "escalations"));
        var file = Path.Combine(dir, "escalations", id + ".json");
        File.WriteAllText(file, card.ToJsonString());

        return file;
    }

    [Fact]
    public void S6_3_ACardWrittenAfterTheMarker_IsWithdrawnAndArchivedAtTheNextV2Start_AndJournaled()
    {
        var dir = Folder();
        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(dir));
        var marker = File.ReadAllBytes(MigrationV2.MarkerFile(dir));
        var late = WriteOpenCard(dir, "bbbbbbbbbbbb");
        MigrateV2Data.Plant("f3", "plan-answered", dir, sessions: false);
        MigrateV2Data.Plant("f3", "plan-auto-released", dir, sessions: false);

        MigrateV2Mode.RunAutomatic(dir, new Version(0, 89, 2), _ => { }, MigrateV2Data.Sink(dir));
        File.Exists(late).Should().BeTrue("a v1 build never migrates, and never sweeps");

        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(dir));

        File.Exists(late).Should().BeFalse("the marker is there, and the start still withdraws an open card at the top level");
        var archived = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "escalations", "archive", "bbbbbbbbbbbb.json")))!.AsObject();
        archived["status"]!.GetValue<string>().Should().Be("withdrawn_v2");
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Should().Contain(l => l.Kind == "card" && l.Id == "bbbbbbbbbbbb" && l.State == "done");
        File.ReadAllBytes(MigrationV2.MarkerFile(dir)).Should().Equal(marker, "the sweep is not a migration: the marker is not rewritten");
        MigrateV2Data.Tree(dir).Where(e => e.Key.StartsWith("escalations/", StringComparison.Ordinal) && !e.Key.StartsWith("escalations/archive/", StringComparison.Ordinal))
            .Should().HaveCount(3, "an answered card with its answer, and an expired card, are history and stay where they are");
    }

    [Fact]
    public void S6_3_AStartWhoseSweepFindsNothing_CreatesNoFile()
    {
        var dir = Folder();
        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(dir));
        var before = MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true);

        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(dir));

        MigrateV2Data.Tree(dir, withFolders: true, withBookkeeping: true).Should().Equal(before, "every start of a v2 server looks; a start that has nothing to do writes nothing, not even a journal line");
    }

    [Fact]
    public void S6_3_AStartWhileAnotherMigrationHoldsTheLease_DoesNotSweep_AndDoesNotThrow()
    {
        var dir = Folder();
        MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(dir));
        var late = WriteOpenCard(dir, "cccccccccccc");
        using var other = HeldFile.TryHold(MigrationV2Lease.FileFor(dir));
        other.Should().NotBeNull("this test is the other migration");

        var start = () => MigrateV2Mode.RunAutomatic(dir, new Version(2, 0, 0), _ => { }, MigrateV2Data.Sink(dir));

        start.Should().NotThrow();
        File.Exists(late).Should().BeTrue("the lease is another process's: the next start sweeps");
    }

    // ---------- the lease answers a closed set of three cases ----------

    [Fact]
    public void TakingTheLease_AnswersTaken_ThenBusyWhileHeld_ThenTakenAgainOnceReleased()
    {
        var dir = Folder();

        var first = MigrationV2Lease.Take(dir);
        var taken = first.Should().BeOfType<LeaseTake.Taken>().Subject;
        var second = MigrationV2Lease.Take(dir);
        second.Should().BeOfType<LeaseTake.Busy>().Which.Why.Should().Be(MigrationV2Lease.BusySentence);
        taken.Lease.Dispose();

        using var again = MigrationV2Lease.Take(dir).Should().BeOfType<LeaseTake.Taken>().Subject.Lease;
        again.Should().NotBeNull("a released lease can be taken again — the kernel's hold, not a stale file");
    }

    [Fact]
    public void ADataFolderThatCannotHoldTheLeaseFolder_AnswersUnusable_NamingWhy()
    {
        var dir = Folder();
        File.WriteAllText(Path.Combine(dir, "claims"), "a file where the folder belongs");

        var answer = MigrationV2Lease.Take(dir);

        answer.Should().BeOfType<LeaseTake.Unusable>().Which.Why.Should().NotBeEmpty();
    }

    // ---------- a journal line that could not be written is said, not swallowed ----------

    [Fact]
    public void AJournalLineTheFolderCannotTake_IsReportedThroughTheCallback_AndTheConversionStillStands()
    {
        var (dir, file, state) = MigratedFolderWithALateHold();
        Directory.CreateDirectory(MigrationV2.JournalFile(dir)); // a directory where the journal file belongs: the append is refused
        var said = new List<string>();

        HoldConversion.Journaled(dir, file, "on-load", said.Add, TimeProvider.System);

        said.Should().ContainSingle().Which.Should().Contain("journal").And.Contain(Path.GetFileName(file), "the sentence names what was not written down");
        LegacyHold.Stands(state).Should().BeTrue("this test only drives the journal door; the session file is untouched here");
    }

    [Fact]
    public void AJournalLineTheFolderCanTake_SaysNothing()
    {
        var (dir, file, _) = MigratedFolderWithALateHold();
        var said = new List<string>();

        HoldConversion.Journaled(dir, file, "on-load", said.Add, TimeProvider.System);

        said.Should().BeEmpty();
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Should().Contain(l => l.Detail == "on-load");
    }

    // ---------- §5.5 a torn journal tail ----------

    [Fact]
    public void S5_5_ATornJournalTail_IsTruncatedToItsLastNewline_BeforeTheNextLineIsAppended()
    {
        var dir = Folder();
        Directory.CreateDirectory(MigrationV2.MigrationsDir(dir));
        var good = """{"seq":1,"utc":"2026-11-22T10:00:00.0000000Z","kind":"store","id":"qphase","state":"planned","detail":""}""" + "\n";
        File.WriteAllText(MigrationV2.JournalFile(dir), good + "{\"seq\":2,\"ut");

        new MigrationV2Journal(MigrationV2.JournalFile(dir), TimeProvider.System).Append("store", "cadence", "planned", string.Empty);

        var text = File.ReadAllText(MigrationV2.JournalFile(dir));
        text.Should().StartWith(good);
        text.Should().NotContain("\"ut\n", "the torn bytes are cut away, not ended with a newline and left in the file");
        text.EndsWith('\n').Should().BeTrue();
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2).And.OnlyContain(l => JsonDocument.Parse(l).RootElement.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public void S5_5_ACrashDuringAJournalWrite_ThenTwoRestarts_LeavesEveryLineParsing()
    {
        var dir = Folder();
        MigrateV2Data.Mixed(dir);
        var journal = MigrationV2.JournalFile(dir);
        var killed = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (n == 8) { throw new SimulatedKill(n); } }).Apply();
        killed.Should().Throw<SimulatedKill>();
        File.AppendAllText(journal, "{\"seq\":99,\"utc\":\"2026-11-2"); // the write the kill cut short

        var again = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (n == 6) { throw new SimulatedKill(n); } }).Apply();
        again.Should().Throw<SimulatedKill>("restart 1 is killed too");
        File.AppendAllText(journal, "{\"seq\":98,\"utc\":\"2026-11-2"); // and its last write is torn as well
        MigrateV2Data.Apply(dir).Status.Should().Be("done"); // restart 2

        var lines = File.ReadAllLines(journal).Where(l => l.Length > 0).ToList();
        lines.Should().OnlyContain(l => IsJsonObject(l), "every line parses: no torn fragment survives to sit in the middle of the file");
        MigrationV2Journal.Read(journal).Select(l => l.Seq).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        JournalLines(dir).Length.Should().Be(MigrationV2Journal.Read(journal).Count, "Read skipped nothing");
    }

    private static bool IsJsonObject(string line)
    {
        try
        {
            using var parsed = JsonDocument.Parse(line);

            return parsed.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
