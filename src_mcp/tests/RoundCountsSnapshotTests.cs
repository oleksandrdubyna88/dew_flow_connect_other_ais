using System.Globalization;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using CoaiMcp.Runners.Files;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Serilog;
using Xunit;
using static CoaiMcp.Tests.RoundCountsSnapshotFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// The snapshot a store leaves for the OTHER side's counter (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>): the last seven days
/// of rounds as a plain JSON file beside <c>coai.db</c>, written after a round and never half-written.
/// </summary>
/// <remarks>
/// Real SQLite over a temp directory and a constant clock handed to the writer. The engine's own write point — after the projection,
/// failing without taking the round down — is <see cref="ARoundLeavesASnapshotBesideTheStoreTests"/>'s; what is here is the file.
/// </remarks>
public sealed class RoundCountsSnapshotTests : IDisposable
{
    private readonly TempDir _store = TempDir.For("testsToDelete-snapshot-");
    private readonly ListSink _sink = new();
    private readonly ILogger _log;

    public RoundCountsSnapshotTests() =>
        _log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _store.Dispose();
    }

    private string SnapshotFile => RoundCountsSnapshot.PathIn(_store.Path);

    private void Live(string key, DateTime started, string stage = nameof(Stage.CodeReview), params ReviewerState[] reviewers)
    {
        using var db = RoundsDb.Open(_store.Path, Serilog.Core.Logger.None)!;
        Record(db, "s" + Guid.NewGuid().ToString("N")[..6], stage, started, key, reviewers.Length == 0 ? [Reviewer("codex")] : reviewers);
    }

    private RoundCountsSnapshotDocument Written()
    {
        return RoundCountsSnapshot.Read(SnapshotFile).Should().BeOfType<SnapshotReading.Loaded>("the snapshot reads back").Which.Document;
    }

    [Fact]
    public void ASnapshot_HasTheDocumentedShape_AndNothingMore()
    {
        Live(Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Reviewer("codex", "gpt-6-luna"), Reviewer("gemini", "gemini-3.8-flash-medium"));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        var json = Parse(File.ReadAllText(SnapshotFile));
        json.EnumerateObject().Select(p => p.Name).Should().Equal(
            "schema", "writtenUtc", "coaiMcpVersion", "store", "fromUtc", "truncated", "rounds");
        json.GetProperty("schema").GetInt32().Should().Be(1);
        json.GetProperty("writtenUtc").GetString().Should().Be(Now.ToString("O"));
        json.GetProperty("fromUtc").GetString().Should().Be(Now.AddDays(-7).ToString("O"));
        json.GetProperty("truncated").GetBoolean().Should().BeFalse();
        json.GetProperty("store").EnumerateObject().Select(p => p.Name).Should().Equal("id", "platform", "host", "wslDistro", "side", "directory");
        var round = json.GetProperty("rounds").EnumerateArray().Single();
        round.EnumerateObject().Select(p => p.Name).Should().Equal("id", "key", "startedUtc", "stage", "reviewers");
        round.GetProperty("key").GetString().Should().Be(Key('a'));
        round.GetProperty("startedUtc").GetString().Should().Be(Now.AddHours(-1).ToString("O"));
        round.GetProperty("stage").GetString().Should().Be("CodeReview");
        round.GetProperty("reviewers").EnumerateArray().Select(r => (r.GetProperty("provider").GetString(), r.GetProperty("model").GetString()))
            .Should().BeEquivalentTo([("codex", "gpt-6-luna"), ("gemini", "gemini-3.8-flash-medium")]);
        File.ReadAllBytes(SnapshotFile)[0].Should().Be((byte)'{', "UTF-8 without a byte-order mark: the document begins at its first byte");
    }

    [Fact]
    public void TheStore_IsNamedByItsOwnersHostAndDirectory()
    {
        Live(Key('a'), Now.AddHours(-1));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        var store = Written().Store;
        store.Id.Should().Be(RoundAuthorsMerge.SourceIdOf(store.Platform, store.WslDistro, _store.Path), "the id rule is the repository's one, over the host as well as the directory");
        store.Id.Should().NotBe(RoundAuthorsMerge.SourceIdOf(_store.Path), "and it is not the directory alone: two WSL distributions share one directory");
        store.Directory.Should().Be(Path.GetFullPath(_store.Path), "the owner's own spelling");
        store.Id.Should().Be(RoundCountsSnapshot.StoreOf(_store.Path, Environment.GetEnvironmentVariable).Id);
    }

    [Fact]
    public void TheIdOfAStore_ChangesWithTheDistribution_AndWithTheDirectory_ButNotWithAnotherSpellingOfTheSamePath()
    {
        var ubuntu = RoundAuthorsMerge.SourceIdOf("linux", "Ubuntu", "/home/u/.local/share/coai-mcp");

        RoundAuthorsMerge.SourceIdOf("linux", "Debian", "/home/u/.local/share/coai-mcp").Should().NotBe(ubuntu);
        RoundAuthorsMerge.SourceIdOf("linux", "Ubuntu", "/home/u/.local/share/other").Should().NotBe(ubuntu);
        RoundAuthorsMerge.SourceIdOf("windows", "Ubuntu", "/home/u/.local/share/coai-mcp").Should().NotBe(ubuntu);
        RoundAuthorsMerge.SourceIdOf("linux", "Ubuntu", "/home/u/.local/share/coai-mcp/").Should().Be(ubuntu, "a trailing separator is the same directory");
        ubuntu.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void OnlyTheLastSevenDays_AreInTheSnapshot()
    {
        Live(Key('a'), Now.AddDays(-6).AddHours(-23));
        Live(Key('b'), Now.AddDays(-7).AddHours(-1));
        Live(Key('c'), Now.AddHours(1));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        var document = Written();
        document.Rounds.Select(r => r.Key).Should().Equal(Key('a'));
        document.FromUtc.Should().Be(Now.AddDays(-7).ToString("O"));
    }

    [Fact]
    public void TheRounds_AreNewestFirst_EachWithItsReviewers()
    {
        Live(Key('a'), Now.AddHours(-3));
        Live(Key('b'), Now.AddHours(-1), nameof(Stage.PlanReview), Reviewer("codex"), Reviewer("codex", role: "SecurityReliability"));
        Live(Key('c'), Now.AddHours(-2));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        var rounds = Written().Rounds;
        rounds.Select(r => r.Key).Should().Equal(Key('b'), Key('c'), Key('a'));
        rounds[0].Stage.Should().Be("PlanReview");
        rounds[0].Reviewers.Should().HaveCount(2);
    }

    [Fact]
    public void ARoundOfNoKey_IsInTheSnapshot_WithAnEmptyKey()
    {
        Live(string.Empty, Now.AddHours(-1));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        Written().Rounds.Should().ContainSingle().Which.Key.Should().BeEmpty();
    }

    [Fact]
    public void PastTheCap_TheNewestAreKept_AndTruncatedIsSaid()
    {
        InsertRounds(RoundCountsSnapshot.MaxRounds + 1);

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        var document = Written();
        document.Rounds.Should().HaveCount(RoundCountsSnapshot.MaxRounds);
        document.Truncated.Should().BeTrue();
        document.Rounds[0].Id.Should().Be(RoundCountsSnapshot.MaxRounds + 1, "newest first");
        document.Rounds.Select(r => r.Id).Should().NotContain(1, "the oldest is the one the cap dropped");
    }

    [Fact]
    public void AtTheCap_NothingIsDropped_AndTruncatedIsFalse()
    {
        InsertRounds(RoundCountsSnapshot.MaxRounds);

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        var document = Written();
        document.Rounds.Should().HaveCount(RoundCountsSnapshot.MaxRounds);
        document.Truncated.Should().BeFalse();
    }

    /// <summary>Rounds 1..n, one minute apart, ending an hour before the clock, in ONE transaction: n = 5 001 is the cap's edge.</summary>
    private void InsertRounds(int count)
    {
        using (var open = RoundsDb.Open(_store.Path, Serilog.Core.Logger.None)!)
        {
            open.Should().NotBeNull();
        }

        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={Path.Combine(_store.Path, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var transaction = db.BeginTransaction();
        using var session = db.CreateCommand();
        session.Transaction = transaction;
        session.CommandText = "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z')";
        session.ExecuteNonQuery();
        using var round = db.CreateCommand();
        round.Transaction = transaction;
        round.CommandText = "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES ($id, 's', 'CodeReview', $id, 'done', 'proceed', $at, $at)";
        var id = round.Parameters.Add("$id", SqliteType.Integer);
        var at = round.Parameters.Add("$at", SqliteType.Text);
        using var reviewer = db.CreateCommand();
        reviewer.Transaction = transaction;
        reviewer.CommandText = "INSERT INTO reviewers (round_id, provider, role, status) VALUES ($id, 'codex', 'Architecture', 'done')";
        var reviewerRound = reviewer.Parameters.Add("$id", SqliteType.Integer);
        for (var n = 1; n <= count; n++)
        {
            id.Value = n;
            at.Value = Now.AddHours(-1).AddMinutes(n - count).ToString("O", CultureInfo.InvariantCulture);
            round.ExecuteNonQuery();
            reviewerRound.Value = n;
            reviewer.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    [Fact]
    public void NoDatabase_NoSnapshot_AndNoFolderIsMade()
    {
        var missing = Path.Combine(_store.Path, "never-made");

        RoundCountsSnapshot.TryWrite(missing, Now, _log);

        Directory.Exists(missing).Should().BeFalse();
        _sink.Lines.Should().BeEmpty("a store with no database has nothing to leave, and that is not a fault");
    }

    [Fact]
    public void TheSnapshot_IsWrittenWhole_OrTheOldOneStays()
    {
        Live(Key('a'), Now.AddHours(-3));
        RoundCountsSnapshot.TryWrite(_store.Path, Now.AddHours(-2), _log);
        var before = File.ReadAllBytes(SnapshotFile);
        Live(Key('b'), Now.AddHours(-1));

        RoundCountsSnapshot.TryWrite(
            _store.Path, Now, _log,
            (path, text) => AtomicFile.Write(path, text, default, new AtomicSeams { AfterTemp = _ => throw new IOException("the disk went away between the temp and the move") }));

        File.ReadAllBytes(SnapshotFile).Should().Equal(before, "the previous document is intact: a reader sees the old file or the new one");
        Directory.EnumerateFiles(_store.Path, "*.tmp").Should().BeEmpty("a temp that could not be moved is removed");
        _sink.Lines.Should().ContainSingle(line => line.Contains("could not be written", StringComparison.Ordinal));
    }

    /// <summary>
    /// The OTHER side's reader holds the snapshot while this side writes it. <see cref="File.ReadAllText(string)"/> opens with
    /// <see cref="FileShare.Read"/>, which forbids every writer to open the file for as long as the reader holds it; the reader therefore
    /// opens it sharing write and delete (<see cref="RoundCountsSnapshot.OpenForRead"/>, the open <c>Read</c> makes), and this test holds the
    /// file exactly so.
    /// </summary>
    /// <remarks>
    /// What sharing does NOT buy, measured 2026-10-22 (Windows 11 26300, .NET 10): a <c>File.Move</c> over a file another handle holds is refused
    /// whatever share mode the holder used — all four combinations of <c>Read</c>, <c>Write</c> and <c>Delete</c> throw. The owner's replace is
    /// covered by <see cref="AtomicFile.SharingRetry"/> (the next test), not by the reader's share mode.
    /// </remarks>
    [Fact]
    public void AReaderHoldingTheSnapshot_DoesNotForbidAWriterToOpenIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the share modes are Windows'; .NET's Unix locks are advisory");
        Live(Key('a'), Now.AddHours(-1));
        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);
        using var reader = RoundCountsSnapshot.OpenForRead(SnapshotFile);

        var open = () => new FileStream(SnapshotFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose();

        open.Should().NotThrow("a reader of the snapshot must not be why a writer of it is refused");
    }

    /// <summary>
    /// The owner's replace, which Windows refuses while ANY handle holds the file, lands once a reader that holds it for a read's moment lets go:
    /// the production write retries the move for <see cref="AtomicFile.SharingRetry"/>, and a read is milliseconds.
    /// </summary>
    [Fact]
    public void TheOwnersReplace_LandsWhenAReaderLetsGoInsideTheRetryWindow()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the refusal of a move over a held file is Windows'; .NET's Unix locks are advisory");
        Live(Key('a'), Now.AddHours(-2));
        RoundCountsSnapshot.TryWrite(_store.Path, Now.AddHours(-1), _log);
        Live(Key('b'), Now.AddHours(-1));
        var reader = RoundCountsSnapshot.OpenForRead(SnapshotFile);
        var seams = new AtomicSeams { Pause = _ => reader.Dispose() };

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log, (path, text) => AtomicFile.Write(path, text, AtomicFile.SharingRetry, seams));

        _sink.Lines.Should().BeEmpty("the reader let go inside the window, so the write was never refused for good");
        Written().Rounds.Should().HaveCount(2, "the replace landed");
    }

    /// <summary>
    /// The default writer IS <see cref="AtomicFile"/>: only it removes the orphan temp of a writer that died, so a plain
    /// <c>File.WriteAllText</c> in its place would leave this one standing.
    /// </summary>
    [Fact]
    public void TheDefaultWriter_IsTheAtomicOne()
    {
        Live(Key('a'), Now.AddHours(-3));
        var orphan = Path.Combine(_store.Path, $"{RoundCountsSnapshot.FileName}.2000000000.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(orphan, "{\"an interrupted");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        File.Exists(orphan).Should().BeFalse("the record's next atomic write owns the clean-up of a dead writer's temp");
        Written().Rounds.Should().ContainSingle();
    }

    [Fact]
    public void AFailedWrite_LogsTheExceptionsTypeOnly_NeverItsMessageOrPath()
    {
        Live(Key('a'), Now.AddHours(-1));
        Directory.CreateDirectory(SnapshotFile);

        var act = () => RoundCountsSnapshot.TryWrite(_store.Path, Now, _log);

        act.Should().NotThrow("a courtesy file must never take a round down");
        var line = _sink.Lines.Should().ContainSingle().Which;
        line.Should().StartWith("the round-counts snapshot could not be written: ");
        line.Should().NotContain(_store.Path, "a filesystem message carries the destination path, and with it a person's name");
        _sink.Levels.Should().Equal(Serilog.Events.LogEventLevel.Warning);
        _sink.Events.Single().Exception.Should().BeNull("the type is logged, the exception object is not");
    }

    /// <summary>A filesystem message carries the destination path (and with it a person's name); whichever exception it is, only its TYPE is logged.</summary>
    [Fact]
    public void AnExceptionsMessage_IsNeverLogged_OnlyItsType()
    {
        Live(Key('a'), Now.AddHours(-1));

        RoundCountsSnapshot.TryWrite(_store.Path, Now, _log, (path, _) => throw new IOException($"cannot write '{path}' for someone-in-particular"));

        _sink.Lines.Should().ContainSingle().Which.Should().Be("the round-counts snapshot could not be written: IOException");
    }

    /// <summary>
    /// A filesystem message names the file it was resolved to, and the file's path names a person; the refusal is echoed into the answer the
    /// extension shows. So its detail is the exception's TYPE and a sentence of ours — never the message.
    /// </summary>
    [Theory]
    [InlineData(typeof(IOException), "the file could not be read (IOException)")]
    [InlineData(typeof(UnauthorizedAccessException), "the file could not be read (UnauthorizedAccessException)")]
    public void AnUnreadableFile_IsRefusedWithTheExceptionsTypeAndNeverItsMessage(Type thrown, string sentence)
    {
        var path = Write(_store.Path, Document(Wsl("Ubuntu"), Now), "locked.json");
        const string Message = @"Access to the path 'C:\Users\alice\AppData\coai-mcp\locked.json' is denied.";

        var read = RoundCountsSnapshot.Read(path, _ => throw (Exception)Activator.CreateInstance(thrown, Message)!);

        var refusal = read.Should().BeOfType<SnapshotReading.Refused>().Which.Refusal;
        refusal.Reason.Should().Be(RoundCountsSnapshot.Unreadable);
        refusal.Detail.Should().Be(sentence);
        refusal.Detail.Should().NotContain("alice").And.NotContain(Message);
    }

    [Fact]
    public void AMissingFile_IsRefusedWithThePathTheCallerPassed()
    {
        var path = Path.Combine(_store.Path, "nowhere", RoundCountsSnapshot.FileName);

        RoundCountsSnapshot.Read(path).Should().BeOfType<SnapshotReading.Refused>().Which.Refusal
            .Should().Be(new SnapshotRefusal(RoundCountsSnapshot.Missing, path));
    }

    [Fact]
    public void ASnapshotReadBack_IsTheDocumentThatWasWritten()
    {
        var document = Document(
            Wsl("Ubuntu"), Now,
            Round(2, Key('b'), Now.AddHours(-1), nameof(Stage.PlanReview), Ran("codex", string.Empty)),
            Round(1, string.Empty, Now.AddHours(-2), nameof(Stage.CodeReview), Ran("gemini")));
        var path = Write(_store.Path, document, "elsewhere.json");

        var read = RoundCountsSnapshot.Read(path);

        read.Should().BeOfType<SnapshotReading.Loaded>().Which.Document.Should().BeEquivalentTo(document);
    }

    [Fact]
    public void ASnapshotsStamps_AreNormalisedToTheColumnsOwnSpelling()
    {
        var document = Document(Wsl("Ubuntu"), Now, Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran("codex")));
        var text = RoundCountsSnapshot.Serialize(document).Replace(Now.AddHours(-1).ToString("O"), "2026-10-21T08:20:00Z", StringComparison.Ordinal);
        var path = Path.Combine(_store.Path, "plain-z.json");
        File.WriteAllText(path, text);

        var read = RoundCountsSnapshot.Read(path);

        read.Should().BeOfType<SnapshotReading.Loaded>().Which.Document.Rounds.Single().StartedUtc.Should().Be(Now.AddHours(-1).ToString("O"), "windows compare these as text, so they must all be spelt one way");
    }

    /// <summary>A valid snapshot's text with one node replaced — the way to say a <c>null</c> element, which the typed fixtures cannot build.</summary>
    private string Tampered(Action<JsonNode> change)
    {
        var node = JsonNode.Parse(RoundCountsSnapshot.Serialize(
            Document(Wsl("Ubuntu"), Now, Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran("codex")))))!;
        change(node);
        var path = Path.Combine(_store.Path, "tampered.json");
        File.WriteAllText(path, node.ToJsonString());

        return path;
    }

    private static void ShouldBeRefusedAsNotASnapshot(SnapshotReading read) =>
        read.Should().BeOfType<SnapshotReading.Refused>().Which.Refusal
            .Should().Be(new SnapshotRefusal(RoundCountsSnapshot.Unreadable, "not a round-counts snapshot"));

    /// <summary>The nullable annotations reject a null PROPERTY but not a null ELEMENT of a list, so a null round got as far as the shape checks and crashed them.</summary>
    [Fact]
    public void ANullRound_IsUnreadable_NotACrash()
    {
        var path = Tampered(node => node["rounds"] = new JsonArray((JsonNode?)null));

        var read = () => RoundCountsSnapshot.Read(path);

        read.Should().NotThrow("a file from the other side is data, not a promise: a null round is a refusal");
        ShouldBeRefusedAsNotASnapshot(RoundCountsSnapshot.Read(path));
    }

    [Fact]
    public void ANullReviewer_IsUnreadable_NotACrash()
    {
        var path = Tampered(node => node["rounds"]![0]!["reviewers"] = new JsonArray((JsonNode?)null));

        var read = () => RoundCountsSnapshot.Read(path);

        read.Should().NotThrow("a null reviewer is a refusal, not a NullReferenceException in the merge");
        ShouldBeRefusedAsNotASnapshot(RoundCountsSnapshot.Read(path));
    }

    [Fact]
    public void ANullReviewersArray_IsUnreadable()
    {
        var path = Tampered(node => node["rounds"]![0]!["reviewers"] = null);

        ShouldBeRefusedAsNotASnapshot(RoundCountsSnapshot.Read(path));
    }

    [Fact]
    public void TheJson_ForAWslStore_NamesItsHostAndDistribution()
    {
        var node = JsonNode.Parse(RoundCountsSnapshot.Serialize(Document(Wsl("Ubuntu"), Now)))!;

        node["store"]!["host"]!.GetValue<string>().Should().Be("wsl");
        node["store"]!["wslDistro"]!.GetValue<string>().Should().Be("Ubuntu");
        node["store"]!["platform"]!.GetValue<string>().Should().Be("linux");
    }
}
