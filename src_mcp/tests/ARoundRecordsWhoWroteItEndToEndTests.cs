using System.Text;
using CoaiMcp.Core.Authors;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// End to end (E5.S5 of <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>): ONE real round through <see cref="PanelService"/>
/// and FakeCli over one of E1's fixtures proves the whole chain — the snapshot is in the session file and in the database, no sentinel leaves
/// the reader, a deleted database comes back with the same authors, and a replay adds nothing.
/// </summary>
/// <remarks>
/// <para>The fixture's transcript is copied into a temp Claude home and its checkout <c>repo-a</c> becomes the repository under review. Its
/// instants sit at 2000-01-01, so the stamps are PLACED — shifted by one offset and compressed to under half a second — to fall between the plan
/// round's completion and the code round's start, derived from the clock at run time and never typed (<c>testing.md</c>, <i>a test that reads the
/// clock expires</i>). The id reaches the service as <c>CLAUDE_CODE_SESSION_ID</c> through its INJECTED environment — never on the process, and never
/// as <c>COAI_CALLER_SESSION</c>, which the locator reads as <c>stated id</c>.</para>
/// <para><b>Positive control first:</b> the code round's snapshot is <c>observed</c> with a <c>wrote here</c> row — the fixture was read and
/// accepted, not rejected — and <c>rounds.agent_log</c> DOES contain the sentinel, because that column renders transcript text by design: it
/// proves the sentinel reached the server, so its absence everywhere else means something.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARoundRecordsWhoWroteItEndToEndTests : FakeCliGateBase
{
    private CallerTranscriptFixture _fixture = null!;

    protected override Task<string> PrepareAsync()
    {
        _fixture = CallerTranscriptFixture.Materialise("resumed-agent", Temp);

        return Task.FromResult(Path.Combine(_fixture.Root, "repo-a"));
    }

    private string Caller => _fixture.Rounds.CallerSessionId;

    private Func<string, string?> Environment => name => name == "CLAUDE_CODE_SESSION_ID" ? Caller : null;

    /// <summary>Every stamped line of the fixture moved so its first lands at <paramref name="from"/> and the rest keep their order, 200 times closer.</summary>
    private void Place(DateTime from)
    {
        var files = new[] { TranscriptFile.Main }.Concat(_fixture.AgentKeys.Select(TranscriptFile.Agent)).ToList();
        var stamped = files
            .SelectMany(file => Enumerable.Range(1, _fixture.Lines(file).Count).Select(n => (File: file, Line: n, Stamp: StampOrNull(file, n))))
            .Where(line => line.Stamp is not null)
            .Select(line => (line.File, line.Line, Stamp: line.Stamp!.Value))
            .ToList();
        var first = stamped.Min(line => line.Stamp);
        foreach (var (file, n, stamp) in stamped)
        {
            _fixture.SetStamp(file, n, from + TimeSpan.FromTicks((stamp - first).Ticks / 200));
        }
    }

    private DateTime? StampOrNull(TranscriptFile file, int line)
    {
        try
        {
            return _fixture.StampOf(file, line);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Open, the plan round, its resolution, the transcript placed between the two rounds, the code round — and everything the log said.</summary>
    private async Task<(PanelService Service, ListSink Sink)> RunBothRoundsAsync(Func<Task>? beforeCodeRound = null)
    {
        var sink = new ListSink();
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Place(DateTime.UtcNow.AddHours(1));
        var service = Service(Environment, _fixture.Home.ProjectsDir, log);
        await service.OpenAsync(Repo, "feature", client: "claude-code");
        Parse(await service.ReviewPlanAsync(Repo, "feature", "a plan nobody objects to")).GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(Repo, "feature", "[]");
        Place(Saved().Rounds.Single().CompletedUtc.AddMilliseconds(20));
        await Task.Delay(700);
        if (beforeCodeRound is not null)
        {
            await beforeCodeRound();
        }

        var answer = Parse(await service.ReviewCodeAsync(Repo, "feature", "main", Scope));
        answer.GetProperty("verdict").GetString().Should().Be("proceed", $"the code round answered: {answer}");

        return (service, sink);
    }

    private RoundRecord CodeRound => Saved().Rounds.Single(round => round.Stage == "CodeReview");

    private string DatabaseFile => Path.Combine(Data, RoundsDb.FileName);

    // ---------- positive control ----------

    [Fact]
    public async Task TheCodeRoundIsObservedWithAWroteHereRow_AndTheSentinelReachedTheServer()
    {
        await RunBothRoundsAsync();

        var authors = CodeRound.Authors;
        authors.Should().NotBeNull("a settled code round carries a snapshot — in the session file");
        authors!.Render().Should().Be("observed");
        authors.Rows.Should().Contain(row => row.Association == Association.WroteHere && row.Who == "subagent" && row.Model == "claude-sonnet-5-5",
            "positive control: the fixture was READ and accepted — its resumed Sonnet agent wrote into the checkout");
        authors.Session.Should().Be(Caller);
        QueryOne("SELECT authors_state FROM rounds WHERE stage = 'CodeReview'").Should().Be("observed", "and in the database");
        QueryOne("SELECT COUNT(*) FROM round_authors WHERE round_id = (SELECT id FROM rounds WHERE stage = 'CodeReview')").Should().Be(authors.Rows.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        QueryOne("SELECT agent_log FROM rounds WHERE stage = 'CodeReview'").Should().Contain(FixtureManifest.Sentinel,
            "rounds.agent_log renders transcript text by design: the sentinel DID reach the server, so its absence below is evidence");
    }

    // ---------- text-free ----------

    [Fact]
    public async Task NoSentinelLeavesTheReader_NotIntoTheSessionFile_TheLog_TheLogLines_OrTheDatabase()
    {
        var (_, sink) = await RunBothRoundsAsync();
        var sentinel = FixtureManifest.Sentinel;

        File.ReadAllText(Directory.GetFiles(Path.Combine(Data, "sessions"), "session-*.json").Single()).Should().NotContain(sentinel, "the gate session's file — the one SessionStore writes");
        OneShotProcess.Run(Data, "--log", "--paged").Out.Should().NotContain(sentinel, "--log's stdout");
        sink.Lines.Should().NotContain(line => line.Contains(sentinel, StringComparison.Ordinal), "every line the service logged, Debug included");
        sink.Lines.Should().Contain(line => line.Contains("agent files empty", StringComparison.Ordinal), "and the Debug lines were captured at all");

        // The one column that carries text by design is blanked, so what remains is what THIS epic wrote.
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False"))
        {
            db.Open();
            Exec(db, "UPDATE rounds SET agent_log = ''");
            Exec(db, "PRAGMA wal_checkpoint(TRUNCATE)");
            Exec(db, "VACUUM");
        }

        SqliteConnection.ClearAllPools();
        var bytes = Directory.GetFiles(Data, RoundsDb.FileName + "*").SelectMany(File.ReadAllBytes).ToArray();
        Contains(bytes, Encoding.UTF8.GetBytes(sentinel)).Should().BeFalse("the database's bytes, with its one text column blanked, checkpointed and vacuumed");
    }

    // ---------- rebuild and replay ----------

    [Fact]
    public async Task ADeletedDatabase_ComesBackWithTheSameAuthors_AndAReplayAddsNothing()
    {
        await RunBothRoundsAsync();
        var first = AuthorsRows();
        first.Should().Contain(row => row.StartsWith("round_authors|", StringComparison.Ordinal), "something to rebuild");

        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Data, RoundsDb.FileName + "*"))
        {
            File.Delete(file);
        }

        var loaded = Saved();
        Reproject(loaded);
        var rebuilt = AuthorsRows();
        Reproject(loaded);

        rebuilt.Should().Equal(first, "the authors columns and rows come back from the session file alone, the ambiguous totals included");
        AuthorsRows().Should().Equal(first, "recording the same rounds again adds no row and changes no total");
    }

    private void Reproject(PersistedSession session)
    {
        using var db = RoundsDb.Open(Data, Serilog.Core.Logger.None)!;
        foreach (var round in session.Rounds)
        {
            db.RecordRound(session.State, round, []);
        }
    }

    // ---------- a failed projection ----------

    [Fact]
    public async Task AFailedProjection_KeepsTheSnapshotInTheFile_WithItsPeerGates()
    {
        var restore = false;
        var peerStarted = DateTime.UtcNow.AddMinutes(-30);
        try
        {
            await RunBothRoundsAsync(() =>
            {
                new PeerWorld(Data, Caller).Add("peerB", peerStarted.AddMinutes(-5), [new RoundRecord("CodeReview", 1, "proceed", 0, "answered", peerStarted.AddMinutes(1)) { StartedUtc = peerStarted }]);
                File.SetAttributes(DatabaseFile, FileAttributes.ReadOnly);
                restore = true;

                return Task.CompletedTask;
            });
        }
        finally
        {
            if (restore)
            {
                File.SetAttributes(DatabaseFile, FileAttributes.Normal);
            }
        }

        var authors = CodeRound.Authors;
        authors.Should().NotBeNull("the round answered, and its session file holds the snapshot though the projection could not be written");
        authors!.PeerGates.Should().Equal(["peerB"], "with the peer set the round saw, so E7 can tell later whether it grew");
        QueryOne("SELECT COUNT(*) FROM rounds WHERE stage = 'CodeReview' AND session_id = '" + Saved().State.SessionId + "'").Should().Be("0", "the database never got the code round — the file is the truth");
    }

    // ---------- helpers ----------

    private List<string> AuthorsRows()
    {
        const string Columns =
            "authors_state, authors_session, authors_id_source, authors_reader, authors_attribution, submodule_writes, uncovered_writes, "
            + "ambiguous_agents, ambiguous_launches, ambiguous_turns, ambiguous_input_tokens, ambiguous_output_tokens, "
            + "ambiguous_cache_creation_tokens, ambiguous_cache_read_tokens";

        return
        [
            .. Rows($"SELECT 'rounds', stage, number, {Columns} FROM rounds ORDER BY stage, number"),
            .. Rows("SELECT 'round_authors', r.stage, r.number, a.* FROM round_authors a JOIN rounds r ON r.id = a.round_id ORDER BY r.stage, r.number, a.ordinal"),
        ];
    }

    private string QueryOne(string sql) => Rows(sql).Single();

    private List<string> Rows(string sql)
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var values = new List<string>();
        while (rows.Read())
        {
            values.Add(string.Join('|', Enumerable.Range(0, rows.FieldCount).Select(at => rows.IsDBNull(at) ? "NULL" : rows.GetValue(at).ToString())));
        }

        return values;
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
