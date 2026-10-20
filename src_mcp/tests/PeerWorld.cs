using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Tests;

/// <summary>
/// Other gate sessions of one caller, as the live path finds them (E5.S4): each a SESSION FILE written through <see cref="SessionStore"/> and
/// the rows <c>RecordRound</c> projects for its rounds — so the lookup is proved against what the real writers leave, not against a table
/// somebody typed. A round that is projected carries an <see cref="AuthorsSnapshot"/> naming the caller, which is what puts its
/// <c>rounds.authors_session</c> there.
/// </summary>
internal sealed class PeerWorld(string dataDir, string caller)
{
    private readonly SessionStore _store = new(dataDir);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    /// <summary>The repository path of peer <paramref name="id"/> — nothing needs to exist there: a session file is addressed by the string.</summary>
    public static string RepoOf(string id) => "D:/peers/" + id;

    /// <summary>
    /// One gate session: <paramref name="rounds"/> go into its file; those in <paramref name="projected"/> are also recorded in the database
    /// under the caller. A file's <c>OpenedUtc</c> is what the lookup must use — the database's own stamp is the time of the recording.
    /// </summary>
    public PersistedSession Add(string id, DateTime openedUtc, IReadOnlyList<RoundRecord> rounds, IReadOnlyList<RoundRecord>? projected = null)
    {
        var state = new SessionState(id, RepoOf(id), "feat/" + id, new PanelConfig()) { Stage = Stage.CodeReview };
        var session = new PersistedSession(state, [.. rounds]) { OpenedUtc = openedUtc };
        _store.Save(session);
        Project(state, projected ?? rounds);

        return session;
    }

    /// <summary>The database rows of rounds as the projection writes them, each naming this world's caller.</summary>
    public void Project(SessionState state, IEnumerable<RoundRecord> rounds)
    {
        using var db = RoundsDb.Open(dataDir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(state, round with { Authors = AuthorsSnapshotFixtures.Observed(round.Stage, round.Number, caller) }, []);
        }
    }

    /// <summary>The peer's session file text, for a test that proves nothing wrote to it.</summary>
    public string FileOf(string id) =>
        File.ReadAllText(Directory.GetFiles(Path.Combine(dataDir, "sessions"), "session-*.json").Single(file => File.ReadAllText(file).Contains($"\"sessionId\": \"{id}\"", StringComparison.Ordinal)));

    /// <summary>Everything the database holds for a gate session's rounds, one string per row — byte equality of two calls is "nothing was written".</summary>
    public List<string> RowsOf(string id)
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT 'round', * FROM rounds WHERE session_id = $id ORDER BY id";
        read.Parameters.AddWithValue("$id", id);
        var rows = new List<string>();
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? "NULL" : reader.GetValue(at).ToString())));
        }

        return rows;
    }
}
