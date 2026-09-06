using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoaiServer;

/// <summary>What is kept about one session. Never the token itself.</summary>
/// <param name="Email">The identity every later request is authorised as.</param>
/// <param name="ExpiresUtc">
/// Absolute, from issue. NOT a sliding window: a session that renewed itself on use would let a
/// stolen token live for as long as the thief kept using it, which is the opposite of a deadline.
/// The panel re-mints silently before this passes, so nobody is logged out by it.
/// </param>
/// <param name="LastUsedUtc">
/// Informational, and written back lazily — see <see cref="SessionStore.LastUsedResolution"/>.
/// It never decides anything; <paramref name="ExpiresUtc"/> does.
/// </param>
public sealed record SessionRecord(
    string Email,
    string Name,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset LastUsedUtc);

/// <summary>
/// The server tokens a panel exchanges an identity-provider token for, so that `coai-mcp` — which
/// an MCP client starts, and which has no Microsoft session of its own — has something to present.
/// </summary>
/// <remarks>
/// <para><b>The raw token is never stored.</b> A session's file is named by the token's SHA-256, so
/// a stolen data directory reveals which emails have sessions and nothing anyone could authenticate
/// with. There is no route in this server that returns a token twice.</para>
/// <para><b>Every write is atomic</b> — a temporary file and a rename — because two of a person's
/// windows can be answering at once, and a half-written session file is a person logged out by a
/// race. Raised on this story's plan round by two reviewers independently.</para>
/// </remarks>
public sealed class SessionStore(string dataDir, TimeSpan ttl, Action<string, Exception>? onFailure = null)
{
    /// <summary>
    /// How coarse <see cref="SessionRecord.LastUsedUtc"/> is allowed to be.
    /// </summary>
    /// <remarks>
    /// An hour, because the alternative is a disk write per request — for a field that decides
    /// nothing. A long poll asks every twenty-five seconds; at one write each, a single reviewer
    /// would rewrite its session file a hundred times an hour for a timestamp nobody reads.
    /// </remarks>
    public static readonly TimeSpan LastUsedResolution = TimeSpan.FromHours(1);

    private readonly string _dir = Path.Combine(dataDir, "sessions");

    /// <summary>The file a token lives in: its hash, never itself.</summary>
    public static string FileNameFor(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant() + ".json";

    /// <summary>
    /// A new session for a verified caller: the token, returned exactly once, and its record.
    /// </summary>
    /// <remarks>
    /// The record is persisted BEFORE the token is handed back, and a write that fails throws — a
    /// caller holding a token this server never stored has a credential that can only ever be
    /// refused, which is worse than an error it can retry. (codex, plan round.)
    /// </remarks>
    public (string Token, SessionRecord Record) Issue(string email, string? name, DateTimeOffset nowUtc)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var record = new SessionRecord(email, name ?? string.Empty, nowUtc, nowUtc + ttl, nowUtc);
        Write(FileNameFor(token), record);

        return (token, record);
    }

    /// <summary>
    /// The session a token names, or null — unknown, or past its deadline.
    /// </summary>
    /// <remarks>
    /// An expired file is deleted on the way out rather than left for the sweep: the request that
    /// found it is the cheapest place to notice, and it means an expired token cannot be replayed
    /// against a server that has not swept yet.
    /// </remarks>
    public SessionRecord? Validate(string token, DateTimeOffset nowUtc)
    {
        var name = FileNameFor(token);
        var record = Read(name);
        if (record is null)
        {
            return null;
        }

        if (record.ExpiresUtc <= nowUtc)
        {
            Delete(name);

            return null;
        }

        // Returned as it now IS, not as it was read: a caller handed the pre-write copy would see a
        // last-used that this very call had already moved. Caught by its own test.
        var used = record with { LastUsedUtc = nowUtc };
        if (nowUtc - record.LastUsedUtc >= LastUsedResolution)
        {
            Write(name, used);
        }

        return used;
    }

    /// <summary>
    /// Withdraw a session. False means the file is still there — the token still works.
    /// </summary>
    /// <remarks>
    /// It RETURNS the outcome instead of swallowing it, and the endpoint answers 500 rather than
    /// 204 when it is false. Answering 204 over a failed delete tells a person their credential was
    /// withdrawn while a stolen bearer goes on working until it expires, which is the one lie a
    /// revoke endpoint must not tell. Raised as Blocking on this change's code round.
    /// </remarks>
    public bool Revoke(string token) => Delete(FileNameFor(token));

    /// <summary>Every session whose deadline has passed, gone.</summary>
    /// <returns>How many were removed.</returns>
    public int Sweep(DateTimeOffset nowUtc)
    {
        if (!Directory.Exists(_dir))
        {
            return 0;
        }

        var swept = 0;
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var record = Read(Path.GetFileName(file));
            if (record is null || record.ExpiresUtc <= nowUtc)
            {
                // A file that will not parse is swept too: it is either a torn write from a killed
                // process or something nobody can authenticate with, and both are litter.
                Delete(Path.GetFileName(file));
                swept += 1;
            }
        }

        return swept;
    }

    private SessionRecord? Read(string fileName)
    {
        try
        {
            var path = Path.Combine(_dir, fileName);

            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ServerJsonContext.Default.SessionRecord)
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A session that cannot be read is a session nobody is signed in with — throwing would
            // turn one torn file into every request failing. But it is REPORTED: an unreadable
            // session and an unknown one are the same 401 to the caller and completely different
            // things to an operator, and only the log can tell them apart.
            onFailure?.Invoke($"session file '{fileName}' could not be read", e);

            return null;
        }
    }

    private void Write(string fileName, SessionRecord record)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, fileName);
        // A UNIQUE temporary name, not `path + ".tmp"`: two windows of one person can refresh the
        // same session at once, and a shared temporary name is two writers on one file — the very
        // race the rename was chosen to avoid. (gemini, code round.)
        var temporary = Path.Combine(_dir, $"{fileName}.{Path.GetRandomFileName()}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(record, ServerJsonContext.Default.SessionRecord));
        // Move, not copy-then-delete: a reader sees the old file or the new one, never half of one.
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>True when the file is gone — including when it was never there.</summary>
    /// <remarks>
    /// Absent is success: revoking twice is not an error, and a session that does not exist is
    /// exactly as withdrawn as one that was just removed. Only a filesystem that REFUSED is false.
    /// </remarks>
    private bool Delete(string fileName)
    {
        try
        {
            File.Delete(Path.Combine(_dir, fileName));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            onFailure?.Invoke($"session file '{fileName}' could not be deleted", e);

            return false;
        }
    }
}
