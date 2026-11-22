using System.Text;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Server.Migrations;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the final code round of E1 accepted: ONE conversion boundary for the migration and for convert-on-load (an answered hold is announced, and the notice read back,
/// BEFORE the session leaves its hold, by either door), a load failure that is said through the server log, an injected clock on the load's journal line and notices, and
/// a notice confirmation that reads only the bytes it appended.
/// </summary>
public sealed class MigrateV2FinalRoundTests : IDisposable
{
    private const string AnsweredCard = "20ef85d344a0";

    private const string Code = "v2-converted-answered-hold";

    private readonly TempDir _root = TempDir.For("coai-migrate-v2-final-");

    private int _folders;

    public void Dispose() => _root.Dispose();

    private string Folder()
    {
        var dir = Path.Combine(_root.Path, "data" + _folders++);
        Directory.CreateDirectory(dir);

        return dir;
    }

    private string FolderWithAnAnsweredHold()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-answered", dir);
        LegacyHold.Stands(MigrateV2Data.Load(dir, MigrateV2Data.SessionFile(dir, "plan")).State).Should().BeTrue();

        return dir;
    }

    private static PersistedSession Load(SessionStore store, string dir, string file)
    {
        var state = MigrateV2Data.Load(dir, file).State;

        return store.Load(MigrateV2Data.Repo, state.Branch, state.Document, state.Feature)!;
    }

    private static (ILogger Log, SaidSink Said) Logging()
    {
        var said = new SaidSink(_ => { });

        return (new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(said).CreateLogger(), said);
    }

    private static string JournalText(string dir) =>
        File.Exists(MigrationV2.JournalFile(dir)) ? File.ReadAllText(MigrationV2.JournalFile(dir)) : string.Empty;

    // ---------- 1. one conversion boundary: convert-on-load announces an answered hold first ----------

    [Fact]
    public void ALoad_WhoseNoticeTheWriterRefuses_LeavesTheAnsweredHoldHeld_WritesNoConversion_AndSaysWhy()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);
        var (log, said) = Logging();

        var loaded = Load(new SessionStore(dir) { Notice = _ => false, Log = log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeTrue("the answer is not overridden until the person can be told; v2 code reads a held session as a normal one");
        File.ReadAllBytes(file).Should().Equal(bytes, "no conversion is written");
        JournalText(dir).Should().NotContain("on-load", "nothing was converted, so nothing is journaled");
        MigrateV2Data.NoticesOf(dir, Code).Should().BeEmpty();
        said.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Warning).Which.RenderMessage().Should().Contain(Path.GetFileName(file), "the warning names the session that stayed held");
    }

    [Fact]
    public void ALoad_WhoseNoticeTheWriterAcceptedButTheDiskNeverTook_LeavesTheAnsweredHoldHeld()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);

        var loaded = Load(new SessionStore(dir) { Notice = _ => true, Log = Logging().Log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeTrue("accepted is not written: the line is read back");
        File.ReadAllBytes(file).Should().Equal(bytes);
    }

    [Fact]
    public void ALoadWithNoNoticeSink_LeavesAnAnsweredHoldHeld_BecauseItCannotAnnounceIt()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);

        var loaded = Load(new SessionStore(dir) { Log = Logging().Log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeTrue();
        File.ReadAllBytes(file).Should().Equal(bytes);
    }

    [Fact]
    public void ALoad_WhoseNoticeIsWritten_WritesTheNoticeFirst_ThenConverts_AndOnlyOnce()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var inner = MigrateV2Data.Sink(dir);
        var heldWhenAnnounced = new List<bool>();
        bool Sink(ServerNotice notice)
        {
            heldWhenAnnounced.Add(LegacyHold.Stands(MigrateV2Data.Load(dir, file).State));

            return inner(notice);
        }

        var store = new SessionStore(dir) { Notice = Sink, Log = Logging().Log };
        var loaded = Load(store, dir, file);

        heldWhenAnnounced.Should().Equal([true], "the notice is written while the session is still held");
        LegacyHold.Stands(loaded.State).Should().BeFalse();
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse("the file is converted after the notice");
        var notice = MigrateV2Data.NoticesOf(dir, Code).Should().ContainSingle().Subject;
        notice.GetProperty("subject").GetString().Should().Be(AnsweredCard, "keyed by the card id, like the migration's");
        notice.GetProperty("answer").GetString().Should().Be("fix");
        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Count(l => l.Detail == "on-load").Should().Be(1);

        Load(store, dir, file);
        MigrateV2Data.NoticesOf(dir, Code).Should().ContainSingle("written once");
    }

    [Fact]
    public void ANoticeTheMigrationWroteBeforeItWasKilled_IsNotWrittenAgainByTheLoadThatConverts()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var killed = () => MigrateV2Data.Migration(dir, atBoundary: n => { if (MigrateV2Data.NoticesOf(dir, Code).Count > 0) { throw new SimulatedKill(n); } }).Apply();
        killed.Should().Throw<SimulatedKill>();
        MigrateV2Data.NoticesOf(dir, Code).Should().ContainSingle("the kill fell between the notice and the conversion");
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeTrue();

        var loaded = Load(new SessionStore(dir) { Notice = MigrateV2Data.Sink(dir), Log = Logging().Log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeFalse();
        MigrateV2Data.NoticesOf(dir, Code).Should().ContainSingle("the card id is the key: found, not written again");
    }

    [Fact]
    public void AMigrationThatDeferredAnAnsweredHold_ThenALoadThatCannotAnnounce_ThenAMigrationThatCan_WritesTheNoticeAndConvertsOnce()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        new MigrationV2(dir, _ => { }, _ => false).Apply().Status.Should().Be("partial", "the migration could not confirm the notice and deferred the session");

        var loaded = Load(new SessionStore(dir) { Notice = _ => false, Log = Logging().Log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeTrue("the load did not convert it either");
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeTrue("so the next migration still sees a held session — the notice is not lost");

        MigrateV2Data.Apply(dir).Status.Should().Be("done");

        MigrateV2Data.NoticesOf(dir, Code).Should().ContainSingle();
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse();
    }

    [Fact]
    public void ALoadThatCannotTakeTheClaim_ReturnsAnAnsweredHoldHeld_AndWritesNothing()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var state = MigrateV2Data.Load(dir, file).State;
        var bytes = File.ReadAllBytes(file);
        using var claim = SessionClaim.TryTake(dir, MigrateV2Data.Repo, state.Branch);
        claim.Should().NotBeNull();

        var loaded = Load(new SessionStore(dir) { Notice = MigrateV2Data.Sink(dir), Log = Logging().Log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeTrue("a session in memory is saved by whoever holds the claim, and it must not be saved converted without its notice");
        File.ReadAllBytes(file).Should().Equal(bytes);
        MigrateV2Data.NoticesOf(dir, Code).Should().BeEmpty();
    }

    [Fact]
    public void AHeldSessionWithNoAnsweredCard_IsStillConvertedByALoad_WithNoSinkAtAll()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-pending", dir, cards: false);
        var file = MigrateV2Data.SessionFile(dir, "plan");

        var loaded = Load(new SessionStore(dir) { Log = Logging().Log }, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeFalse("there is no answer to announce, so nothing waits for a notice");
        LegacyHold.Stands(MigrateV2Data.Load(dir, file).State).Should().BeFalse();
    }

    // ---------- 2. a load that cannot persist its conversion says so ----------

    [Fact]
    public void ALoad_ThatCannotSaveTheClaimedSession_ReturnsTheConversionInMemory_AndReportsTheExceptionThroughTheLog()
    {
        var dir = Folder();
        MigrateV2Data.Plant("f3", "plan-pending", dir, cards: false);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);
        var (log, said) = Logging();
        var store = new SessionStore(dir) { Log = log, FileWriter = (_, _) => throw new IOException("the disk is full") };

        var loaded = Load(store, dir, file);

        LegacyHold.Stands(loaded.State).Should().BeFalse("the caller still gets the session as v2 reads it");
        File.ReadAllBytes(file).Should().Equal(bytes);
        var warning = said.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Warning).Subject;
        warning.Exception.Should().BeOfType<IOException>().Which.Message.Should().Contain("the disk is full");
        warning.RenderMessage().Should().Contain(Path.GetFileName(file));
    }

    // ---------- 3. the load's journal line and notices read the injected clock ----------

    [Fact]
    public void TheJournalLineAndTheNoticeOfAConversionOnLoad_AreStampedByTheStoresClock()
    {
        var dir = FolderWithAnAnsweredHold();
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var now = new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var store = new SessionStore(dir) { Notice = MigrateV2Data.Sink(dir), Clock = new ManualClock(now), Log = Logging().Log };

        Load(store, dir, file);

        var line = MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Single(l => l.Detail == "on-load");
        line.Utc.Should().Be(now.UtcDateTime.ToString("O"));
        MigrateV2Data.NoticesOf(dir, Code).Single().GetProperty("utc").GetString().Should().Be(ServerNotice.Iso(now));
    }

    [Fact]
    public void AJournalLineOutsideARun_TakesItsTimeFromTheClockItIsGiven()
    {
        var dir = FolderWithAnAnsweredHold();
        var now = new DateTimeOffset(2032, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Directory.CreateDirectory(MigrationV2.MigrationsDir(dir));

        HoldConversion.Journaled(dir, MigrateV2Data.SessionFile(dir, "plan"), "on-load", _ => { }, new ManualClock(now));

        MigrationV2Journal.Read(MigrationV2.JournalFile(dir)).Single().Utc.Should().Be(now.UtcDateTime.ToString("O"));
    }

    // ---------- 4. a notice is confirmed by its own bytes, not by the whole history ----------

    private static string LargeHistory(string dir, int lines)
    {
        var file = Path.Combine(dir, ServerNotices.Name);
        var text = new StringBuilder();
        for (var i = 0; i < lines; i++)
        {
            text.Append(i % 2 == 0
                ? $$"""{"utc":"2026-11-01T00:00:00.0000000Z","class":"outcome","code":"{{Code}}","subject":"old{{i:D6}}","title":"{{new string('x', 60)}}"}"""
                : $$"""{"utc":"2026-11-01T00:00:00.0000000Z","class":"refusal","code":"refused","subject":"other{{i:D6}}","title":"{{new string('y', 60)}}"}""").Append('\n');
        }

        File.WriteAllText(file, text.ToString());

        return file;
    }

    private static (MigrationV2 Migration, List<(long Offset, long Bytes)> Tails, List<long> Before, Func<int> Scans) Measured(string dir, string notices)
    {
        var tails = new List<(long, long)>();
        var before = new List<long>();
        var scans = 0;
        var inner = MigrateV2Data.Sink(dir);
        var migration = new MigrationV2(dir, _ => { }, notice =>
        {
            before.Add(new FileInfo(notices).Length);

            return inner(notice);
        })
        {
            NoticeLines = folder =>
            {
                scans++;

                return ServerNotices.Lines(folder);
            },
            NoticeTail = (folder, offset) =>
            {
                var read = ServerNotices.LinesFrom(folder, offset).ToList();
                tails.Add((offset, read.Sum(l => (long)Encoding.UTF8.GetByteCount(l) + 1)));

                return read;
            },
        };

        return (migration, tails, before, () => scans);
    }

    [Fact]
    public void AnApplyOverALargeHistory_ReadsItOnce_AndConfirmsEachNoticeFromTheLengthRecordedBeforeItsWrite()
    {
        var dir = FolderWithAnAnsweredHold();
        var notices = LargeHistory(dir, 4000);
        var historyBytes = new FileInfo(notices).Length;
        var (migration, tails, before, scans) = Measured(dir, notices);

        migration.Apply().Status.Should().Be("done");

        scans().Should().Be(1, "the one initial subject scan; no read-back of the whole history");
        before.Should().Equal(historyBytes);
        var appended = new FileInfo(notices).Length - historyBytes;
        tails.Should().ContainSingle();
        tails[0].Offset.Should().Be(historyBytes, "the tail starts where the file ended before the write");
        tails[0].Bytes.Should().BeLessThanOrEqualTo(appended, "the confirm reads at most the appended bytes (here, one line), never the history's " + historyBytes);
        MigrateV2Data.NoticesOf(dir, Code).Should().Contain(n => n.GetProperty("subject").GetString() == AnsweredCard);
    }

    [Fact]
    public void ANoticeTheDiskNeverTook_IsStillRefusedByTheTailRead()
    {
        var dir = FolderWithAnAnsweredHold();
        var notices = LargeHistory(dir, 100);
        var file = MigrateV2Data.SessionFile(dir, "plan");
        var bytes = File.ReadAllBytes(file);
        var migration = new MigrationV2(dir, _ => { }, _ => true);

        migration.Apply().Status.Should().Be("partial");

        File.ReadAllBytes(file).Should().Equal(bytes, "an empty tail is not a confirmation");
        new FileInfo(notices).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TheTailOfTheLiveNoticesFile_StartsAtTheOffset_AndIsTheWholeFileOnceItWasRolled()
    {
        var dir = Folder();
        var file = LargeHistory(dir, 1000);
        var length = ServerNotices.Length(dir);
        length.Should().Be(new FileInfo(file).Length);
        File.AppendAllText(file, """{"code":"later","subject":"s"}""" + "\n");

        ServerNotices.LinesFrom(dir, length).Should().Equal(["""{"code":"later","subject":"s"}"""], "only what was appended after the recorded length");

        File.WriteAllText(file, """{"code":"fresh","subject":"s"}""" + "\n"); // the live file was rolled aside and a new one began
        ServerNotices.LinesFrom(dir, length).Should().Equal(["""{"code":"fresh","subject":"s"}"""], "a file shorter than the offset is a new file: read from its start");
        ServerNotices.Length(Path.Combine(dir, "nowhere")).Should().Be(0);
    }

    [Fact]
    public void AnApplyOverThreeAnsweredHolds_ConfirmsEachFromItsOwnOffset_AndAddsWhatItFoundToTheSet()
    {
        var dir = FolderWithAnAnsweredHold();
        var cards = Path.Combine(dir, "escalations");
        foreach (var id in new[] { "bbbbbbbbbbbb", "cccccccccccc" })
        {
            File.WriteAllText(Path.Combine(cards, id + ".json"), File.ReadAllText(Path.Combine(cards, AnsweredCard + ".json")).Replace(AnsweredCard, id));
            File.WriteAllText(Path.Combine(cards, id + ".answer.json"), File.ReadAllText(Path.Combine(cards, AnsweredCard + ".answer.json")).Replace(AnsweredCard, id));
        }

        var notices = LargeHistory(dir, 500);
        var (migration, tails, before, scans) = Measured(dir, notices);

        migration.Apply().Status.Should().Be("done");

        scans().Should().Be(1);
        tails.Select(t => t.Offset).Should().Equal(before, "each confirm starts at the file length its own write started from");
        tails.Should().HaveCount(3).And.OnlyContain(t => t.Bytes < 1000);
        MigrateV2Data.NoticesOf(dir, Code).Select(n => n.GetProperty("subject").GetString()).Should().Contain([AnsweredCard, "bbbbbbbbbbbb", "cccccccccccc"]);
    }
}
