using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>One finding as the log page shows it — what it said, and what was decided about it.</summary>
public sealed record LoggedFinding(
    int Ordinal,
    string Severity,
    string Category,
    string File,
    int Line,
    string Title,
    string Why,
    string Fix,
    string Role,
    bool IsGating,
    string Providers,
    string Resolution,
    string Reason,
    bool ReRaised);

/// <summary>One round, with the findings it produced. Keyed the way the page keys its own rows.</summary>
public sealed record LoggedRound(
    string RepoPath,
    string Branch,
    string Stage,
    int Number,
    string StartedUtc,
    int Accepted,
    int Rejected,
    IReadOnlyList<LoggedFinding> Findings);

/// <summary>How often one kind of thing was accepted — a category, a role, or a vendor.</summary>
public sealed record BlindSpot(string Kind, string Name, int Accepted, int Total);

/// <summary>What the page asks for in one go.</summary>
public sealed record LoggedLog(
    IReadOnlyList<LoggedRound> Rounds,
    IReadOnlyList<BlindSpot> BlindSpots,
    IReadOnlyList<LoggedFinding> Defended);

/// <summary>
/// Reading the rounds database, for the page and for the two questions it exists to answer.
/// </summary>
/// <remarks>
/// <para><b>Why the server reads it and not the extension.</b> The extension would need SQLite of its
/// own — a WebAssembly build in the VSIX, or a native module per platform — to ask questions of a
/// file this binary already has open and whose schema it owns. One read-only mode here costs sixty
/// lines and no dependency, and it keeps every query beside the table it queries.</para>
/// <para><b>The two questions</b> come from what the data is for (operator, 2026-09-05): finding the
/// blind spots in an AI's own reasoning. An ACCEPTED finding is something the caller had not seen
/// and then agreed was worth having, so accepted-by-category, by-role and by-vendor is the shape of
/// what it habitually misses. A REJECTED finding that a later round raised again is a disagreement
/// the caller is defending, which is the more interesting kind and a much shorter list.</para>
/// </remarks>
public static class RoundsQuery
{
    /// <summary>How many rounds the page is given, newest first.</summary>
    public const int DefaultLimit = 300;

    public static LoggedLog Read(string dataDir, int limit = DefaultLimit)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return new LoggedLog([], [], []);
        }

        using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly");
        db.Open();

        return new LoggedLog(Rounds(db, limit), BlindSpots(db), Defended(db));
    }

    private static List<LoggedRound> Rounds(SqliteConnection db, int limit)
    {
        var findings = FindingsByRound(db, limit);
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT r.id, s.repo_path, s.branch, r.stage, r.number, r.started_utc, r.accepted, r.rejected
            FROM rounds r JOIN sessions s ON s.id = r.session_id
            ORDER BY r.started_utc DESC LIMIT $limit
            """;
        read.Parameters.AddWithValue("$limit", limit);
        using var rows = read.ExecuteReader();
        var rounds = new List<LoggedRound>();
        while (rows.Read())
        {
            rounds.Add(new LoggedRound(
                rows.GetString(1),
                rows.GetString(2),
                rows.GetString(3),
                rows.GetInt32(4),
                rows.GetString(5),
                rows.GetInt32(6),
                rows.GetInt32(7),
                findings.TryGetValue(rows.GetInt64(0), out var mine) ? mine : []));
        }

        return rounds;
    }

    private static Dictionary<long, List<LoggedFinding>> FindingsByRound(SqliteConnection db, int limit)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT f.round_id, f.ordinal, f.severity, f.category, f.file, f.line, f.title, f.why, f.fix,
                   f.role, f.is_gating, f.providers, f.resolution, f.reason, f.re_raised
            FROM findings f
            WHERE f.round_id IN (SELECT id FROM rounds ORDER BY started_utc DESC LIMIT $limit)
            ORDER BY f.round_id, f.ordinal
            """;
        read.Parameters.AddWithValue("$limit", limit);
        using var rows = read.ExecuteReader();
        var byRound = new Dictionary<long, List<LoggedFinding>>();
        while (rows.Read())
        {
            var round = rows.GetInt64(0);
            if (!byRound.TryGetValue(round, out var mine))
            {
                byRound[round] = mine = [];
            }

            mine.Add(FindingFrom(rows, 1));
        }

        return byRound;
    }

    private static LoggedFinding FindingFrom(SqliteDataReader rows, int at) =>
        new(rows.GetInt32(at),
            rows.GetString(at + 1),
            rows.GetString(at + 2),
            rows.GetString(at + 3),
            rows.GetInt32(at + 4),
            rows.GetString(at + 5),
            rows.GetString(at + 6),
            rows.GetString(at + 7),
            rows.GetString(at + 8),
            rows.GetInt32(at + 9) == 1,
            rows.GetString(at + 10),
            rows.GetString(at + 11),
            rows.GetString(at + 12),
            rows.GetInt32(at + 13) == 1);

    /// <summary>
    /// What the caller accepts, grouped three ways.
    /// </summary>
    /// <remarks>
    /// Accepted over TOTAL rather than accepted alone, because a category that produces fifty
    /// findings and gets two accepted says something different from one that produces two and gets
    /// both — and only the second is a blind spot worth acting on.
    /// </remarks>
    private static List<BlindSpot> BlindSpots(SqliteConnection db)
    {
        var spots = new List<BlindSpot>();
        spots.AddRange(GroupedBy(db, "category"));
        spots.AddRange(GroupedBy(db, "role"));
        spots.AddRange(GroupedBy(db, "providers"));

        return spots;
    }

    private static List<BlindSpot> GroupedBy(SqliteConnection db, string column)
    {
        using var read = db.CreateCommand();
        // The column is one of three names written HERE, never anything a caller sends — the only
        // way a column name can be interpolated safely, and it is worth saying out loud.
        read.CommandText = $"""
            SELECT {column}, SUM(resolution = 'accept'), COUNT(*)
            FROM findings WHERE resolution <> '' GROUP BY {column} ORDER BY 2 DESC
            """;
        using var rows = read.ExecuteReader();
        var spots = new List<BlindSpot>();
        while (rows.Read())
        {
            spots.Add(new BlindSpot(column, rows.GetString(0), rows.GetInt32(1), rows.GetInt32(2)));
        }

        return spots;
    }

    /// <summary>Findings a reviewer raised again over a rejection that still stood.</summary>
    private static List<LoggedFinding> Defended(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT ordinal, severity, category, file, line, title, why, fix, role, is_gating,
                   providers, resolution, reason, re_raised
            FROM findings WHERE re_raised = 1 ORDER BY id DESC LIMIT 200
            """;
        using var rows = read.ExecuteReader();
        var defended = new List<LoggedFinding>();
        while (rows.Read())
        {
            defended.Add(FindingFrom(rows, 0));
        }

        return defended;
    }
}
