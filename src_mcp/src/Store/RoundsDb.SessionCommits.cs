using CoaiMcp.Core.Rounds;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The session update a finished round still owes its session file.
/// </summary>
/// <param name="ExpectedSha256">
/// The session file's hash before the round. The document below may only be written over exactly
/// that state; empty means there was no file at all.
/// </param>
/// <param name="ResultSha256">
/// The hash of <paramref name="SessionJson"/>. A file already carrying it has had this update, so a
/// second attempt is a no-op rather than a second round in the trail.
/// </param>
public sealed record SessionCommit(
    RoundLocator Locator,
    string RepoPath,
    string Branch,
    string ExpectedSha256,
    string SessionJson,
    string ResultSha256)
{
    public bool Applied { get; init; }

    public string Conflict { get; init; } = string.Empty;
}

/// <summary>
/// The outbox half: what a completed round owes its session, and whether it has been paid.
/// </summary>
public sealed partial class RoundsDb
{
    /// <summary>The session updates for this repo+branch that are still owed.</summary>
    /// <remarks>
    /// Ordered by creation so a session that owes two rounds is caught up in the order they
    /// finished — applying the later one first would fail the earlier one's expected hash, which is
    /// correct but leaves a conflict where patience would have done.
    /// </remarks>
    public IReadOnlyList<SessionCommit> UnappliedCommits(string repoPath, string branch)
    {
        using var read = _db.CreateCommand();
        read.CommandText = """
            SELECT provider_id, session_id, round_id, repo_path, branch, expected_sha256,
                   session_json, result_sha256, applied, conflict
            FROM session_commits
            WHERE repo_path = $repo AND branch = $branch AND applied = 0
            ORDER BY created_utc
            """;
        Bind(read, "$repo", repoPath);
        Bind(read, "$branch", branch);

        var owed = new List<SessionCommit>();
        using var rows = read.ExecuteReader();
        while (rows.Read())
        {
            owed.Add(new SessionCommit(
                new RoundLocator(rows.GetString(0), rows.GetString(1), rows.GetString(2)),
                rows.GetString(3),
                rows.GetString(4),
                rows.GetString(5),
                rows.GetString(6),
                rows.GetString(7))
            {
                Applied = rows.GetInt32(8) == 1,
                Conflict = rows.GetString(9),
            });
        }

        return owed;
    }

    /// <summary>One commit by its locator, whatever state it is in.</summary>
    public SessionCommit? CommitFor(RoundLocator locator)
    {
        using var read = _db.CreateCommand();
        read.CommandText = """
            SELECT provider_id, session_id, round_id, repo_path, branch, expected_sha256,
                   session_json, result_sha256, applied, conflict
            FROM session_commits
            WHERE provider_id = $provider AND session_id = $session AND round_id = $round
            """;
        BindLocator(read, locator);
        using var rows = read.ExecuteReader();

        return rows.Read()
            ? new SessionCommit(
                new RoundLocator(rows.GetString(0), rows.GetString(1), rows.GetString(2)),
                rows.GetString(3),
                rows.GetString(4),
                rows.GetString(5),
                rows.GetString(6),
                rows.GetString(7))
            {
                Applied = rows.GetInt32(8) == 1,
                Conflict = rows.GetString(9),
            }
            : null;
    }

    /// <summary>Records that the session file now carries this update.</summary>
    /// <remarks>
    /// Idempotent by construction: marking an applied row applied again changes nothing, which is
    /// what makes running recovery twice safe.
    /// </remarks>
    public void MarkApplied(RoundLocator locator)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE session_commits SET applied = 1, applied_utc = $when, conflict = ''
            WHERE provider_id = $provider AND session_id = $session AND round_id = $round
            """;
        Bind(write, "$when", DateTime.UtcNow.ToString("O"));
        BindLocator(write, locator);
        write.ExecuteNonQuery();
    }

    /// <summary>
    /// Records that the session had moved and the update was NOT applied.
    /// </summary>
    /// <remarks>
    /// The row stays unapplied on purpose. A conflict is a fact about a finished round whose session
    /// somebody else advanced, and quietly writing over it would destroy whatever they did.
    /// </remarks>
    public void MarkConflict(RoundLocator locator, string reason)
    {
        using var write = _db.CreateCommand();
        write.CommandText = """
            UPDATE session_commits SET conflict = $reason
            WHERE provider_id = $provider AND session_id = $session AND round_id = $round
              AND applied = 0
            """;
        Bind(write, "$reason", reason);
        BindLocator(write, locator);
        write.ExecuteNonQuery();
    }

    /// <summary>The outbox write, for a caller that already holds the completion's transaction.</summary>
    internal void RecordCommitHere(SessionCommit commit)
    {
        using var write = _db.CreateCommand();
        // The round's answer is written once and so is what it owes the session. A repeated
        // completion of the same locator must not replace the expected hash with today's, which
        // would let the update apply over a state it was never computed from.
        write.CommandText = """
            INSERT INTO session_commits (
                provider_id, session_id, round_id, repo_path, branch,
                expected_sha256, session_json, result_sha256, created_utc)
            VALUES ($provider, $session, $round, $repo, $branch,
                    $expected, $json, $result, $created)
            ON CONFLICT (provider_id, session_id, round_id) DO NOTHING
            """;
        BindLocator(write, commit.Locator);
        Bind(write, "$repo", commit.RepoPath);
        Bind(write, "$branch", commit.Branch);
        Bind(write, "$expected", commit.ExpectedSha256);
        Bind(write, "$json", commit.SessionJson);
        Bind(write, "$result", commit.ResultSha256);
        Bind(write, "$created", DateTime.UtcNow.ToString("O"));
        write.ExecuteNonQuery();
    }
}
