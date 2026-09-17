using Microsoft.Data.Sqlite;

namespace CoaiBugs.Tests;

/// <summary>
/// Reads a database FILE the way an operator's <c>sqlite3</c> would — a separate opener, beside
/// whatever server or corpus already holds it.
/// </summary>
/// <remarks>
/// Its own connection every call, and never through <see cref="Corpus"/>: a test asserting what the
/// migration wrote must read the file with something that does not run the migration on its way in.
/// </remarks>
internal static class TestSql
{
    /// <summary>The first column of every row, as text; an empty string for NULL.</summary>
    public static IReadOnlyList<string> Column(string path, string sql)
    {
        using var db = Open(path);
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var values = new List<string>();
        while (rows.Read())
        {
            values.Add(rows.IsDBNull(0) ? string.Empty : rows.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }

    /// <summary>One value, as text; empty when the query answers no row.</summary>
    public static string Scalar(string path, string sql) => Column(path, sql).SingleOrDefault() ?? string.Empty;

    /// <summary>Runs one or more statements against the file.</summary>
    public static void Run(string path, string sql)
    {
        using var db = Open(path);
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection($"Data Source={path};Pooling=False");
        db.Open();

        return db;
    }
}
