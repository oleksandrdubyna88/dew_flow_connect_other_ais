using System.Security.Cryptography;
using System.Text;

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
    private readonly JsonFileStore _files = new(onFailure);

    /// <summary>The same store with NO reporter — what <see cref="Active"/> reads through. See it for why.</summary>
    private readonly JsonFileStore _quiet = new();

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
            Stamp(name, used);
        }

        return used;
    }

    /// <summary>The lazy last-used write: reported when it cannot land, never a refused request.</summary>
    /// <remarks>
    /// The stamp decides nothing — <see cref="SessionRecord.LastUsedUtc"/> is informational — so a
    /// filesystem that refuses it (on Windows, a reader holding the file without the delete share:
    /// a backup, an editor, an older build's listing) must not log a person out for a timestamp
    /// nobody reads. <see cref="Issue"/> keeps throwing on a failed write, because a token the server
    /// never stored is a credential that can only ever be refused; this write has no such stake.
    /// Caught here and only here, because this is the layer that knows the write was optional.
    /// </remarks>
    private void Stamp(string fileName, SessionRecord used)
    {
        try
        {
            Write(fileName, used);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            onFailure?.Invoke(
                $"'{Path.Combine(_dir, fileName)}' last-used stamp could not be written; the session is still served",
                e);
        }
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

    /// <summary>Every session that has not reached its deadline, read from the files — nothing written back.</summary>
    /// <remarks>
    /// <para>The roster behind <c>GET /api/people</c>, asked about once a minute per open admin page. It
    /// is a READ, and three things it deliberately does not do are each the opposite of a sibling here:</para>
    /// <list type="bullet">
    /// <item><b>It never deletes.</b> <see cref="Sweep"/> removes an expired or torn file and
    /// <see cref="Validate"/> removes an expired one on sight. A listing that deleted would make every
    /// admin's page a second sweep, on a clock nobody chose.</item>
    /// <item><b>It never moves <see cref="SessionRecord.LastUsedUtc"/>.</b> Looking at who is signed in is
    /// not anybody using their session.</item>
    /// <item><b>It never reports a file it could not read.</b> A vanished file is a session revoked or swept
    /// between the directory listing and the read; a torn one is a write a killed process left for the
    /// sweep. Both are expected, and a log line about either sixty times an hour would bury the failures
    /// that matter — so this reads through a store with no reporter, and the shared read path answers
    /// null and says nothing. <see cref="SessionSweeper"/> removes the litter at boot and every hour,
    /// and says so — which is also what keeps this read from walking a week of dead files.</item>
    /// </list>
    /// <para>Expiry is judged here exactly as <see cref="Validate"/> judges it, so a session this lists is
    /// one the STORE would still accept. Whether the server would then serve the caller is the gate's
    /// decision — a domain removed from the allow-list refuses a live session — and the endpoint applies
    /// that test on top; the store knows nothing of domains.</para>
    /// </remarks>
    public IReadOnlyList<SessionRecord> Active(DateTimeOffset nowUtc)
    {
        if (!Directory.Exists(_dir))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(_dir, "*.json")
            .Select(file => _quiet.Read(file, ServerJsonContext.Default.SessionRecord))
            .OfType<SessionRecord>()
            .Where(record => record.ExpiresUtc > nowUtc)];
    }

    // The three below are the shared store with this class's naming applied. They were this class's
    // OWN implementation until story 2.2 needed the identical atomic write for the slot state; the
    // answer was to extract it rather than copy it, so there is one write path and the next fix
    // reaches both. Behaviour is unchanged, which is what this story's session tests prove by
    // passing unedited.
    private SessionRecord? Read(string fileName) =>
        _files.Read(Path.Combine(_dir, fileName), ServerJsonContext.Default.SessionRecord);

    private void Write(string fileName, SessionRecord record) =>
        _files.Write(Path.Combine(_dir, fileName), record, ServerJsonContext.Default.SessionRecord);

    private bool Delete(string fileName) => _files.Delete(Path.Combine(_dir, fileName));
}
