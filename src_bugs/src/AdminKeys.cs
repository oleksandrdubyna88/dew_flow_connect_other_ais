using System.Security.Cryptography;
using System.Text;

namespace CoaiBugs;

/// <summary>
/// The administrators this server will answer to, read once from <c>COAI_BUGS_ADMIN_KEYS</c>.
/// </summary>
/// <remarks>
/// <para><b>Newline-separated, many</b>, because there is more than one administrator and a secret
/// store holds text. Blank lines and <c>#</c> comments are ignored so the variable can be kept
/// legibly in a secret box, with a line per person and a note saying whose it is — the note being a
/// comment, it never reaches this server's memory.</para>
/// <para><b>An ARRAY, never a <c>FrozenSet</c>.</b> A set's <c>Contains</c> is a hash and a probe,
/// and both are data-dependent: the bucket reached depends on the value, so the time taken leaks
/// something about the credential presented. An array walked end to end does not.</para>
/// <para><b>Constant with respect to the CREDENTIAL, which is the guarantee that can be given.</b>
/// The plan's first wording asked for time "independent of how many admins are configured", and two
/// reviewers pointed out that is impossible: N comparisons cost O(N). What matters is that timing
/// reveals nothing about the key PRESENTED — so every configured hash is compared with
/// <see cref="CryptographicOperations.FixedTimeEquals"/> and the results are accumulated with no
/// early return, exactly as <c>Corpus.Matching</c> does for contributor keys. The number of
/// administrators is not a secret: it is in the operator's own secret store, and nothing here claims
/// to hide it.</para>
/// <para><b>Revoking an administrator is NOT immediate.</b> This set is built once at startup and is
/// immutable for the process's lifetime, which is what makes an admin upload safe without the
/// in-force re-check a contributor key needs — there is no window between the gate and the write for
/// it to change in. The cost is the other side of the same coin: removing a line from the variable
/// does nothing until a SUCCESSFUL redeploy, and a deploy that fails or rolls back leaves the
/// credential live. The operator accepted that on 2026-09-17; the emergency measure is stopping the
/// service, and `deploy/bugs/README.md` says so.</para>
/// </remarks>
public sealed class AdminKeys
{
    /// <summary>The environment variable the administrators are read from.</summary>
    public const string Variable = "COAI_BUGS_ADMIN_KEYS";

    /// <summary>One entry per configured administrator: the hash of their key.</summary>
    /// <remarks>
    /// The HASHES, never the keys. The keys arrive as text and are hashed once here, so the only
    /// copy this process keeps of an administrator's credential is one it cannot reverse — the same
    /// treatment `api_keys.key_hash` gives a contributor's.
    /// </remarks>
    private readonly byte[][] _hashes;

    /// <summary>How many administrators are configured. Not a secret; see the remarks.</summary>
    public int Count => _hashes.Length;

    /// <summary>Whether the variable was absent or held no usable line.</summary>
    public bool None => _hashes.Length == 0;

    private AdminKeys(byte[][] hashes) => _hashes = hashes;

    /// <summary>Reads the variable and hashes every configured key with the server's secret.</summary>
    /// <param name="raw">The variable's value, or <c>null</c> when it is absent.</param>
    /// <param name="secret">The server secret, so a stolen database is not a rainbow-table exercise.</param>
    /// <remarks>
    /// An absent variable and one holding only comments come to the same thing — no administrators —
    /// and that is not a startup failure. An admin API with no administrators answers 401 to
    /// everybody, which is a legitimate way to run this server and is the reason
    /// <see cref="None"/> is reported at startup rather than thrown.
    /// </remarks>
    public static AdminKeys Read(string? raw, string secret) =>
        new([.. Lines(raw).Select(key => Encoding.UTF8.GetBytes(Corpus.HashOf(key, secret)))]);

    /// <summary>The administrator a presented key belongs to, or nothing.</summary>
    /// <remarks>
    /// <para>The loop does not stop early, and the result is carried out rather than returned from
    /// inside: a <c>return</c> on the first match would make the answer's timing depend on WHERE in
    /// the configured list the credential sits, which is a thing an attacker can measure and use.
    /// </para>
    /// <para>An empty presented key is refused before any comparison. That is not a timing leak
    /// worth closing — a caller who sent no credential already knows they sent none.</para>
    /// </remarks>
    public Presented Match(string presented, string secret)
    {
        if (presented.Length == 0)
        {
            return new Presented.Unknown();
        }

        var hash = Corpus.HashOf(presented, secret);
        var wanted = Encoding.UTF8.GetBytes(hash);
        var found = false;
        foreach (var candidate in Probed)
        {
            // Accumulated, never returned from inside, and `|=` rather than a short-circuiting
            // `||` so every entry is really compared.
            found |= CryptographicOperations.FixedTimeEquals(candidate, wanted);
        }

        return found ? new Presented.Administrator(hash) : new Presented.Unknown();
    }

    /// <summary>What a presented credential is compared against — never an empty list.</summary>
    /// <remarks>
    /// <para><b>An unconfigured server does the same work as a configured one.</b> This used to
    /// return early when nothing was configured, which meant it never computed the presented key's
    /// hash at all — a whole HMAC and a hex formatting skipped — while every configured deployment
    /// paid for it on each attempt. The gate answers both cases with the same status and the same
    /// body, so the disclosure the contract forbids was closed on the bytes and left open on the
    /// clock; and because a refused credential is never rate-limited, an attacker can average as
    /// many attempts as they like. (Code round, codex.)</para>
    /// <para>So an empty configuration is compared against ONE stand-in of the same length, and the
    /// path taken is the path of a server with one administrator and a wrong key presented. That is
    /// the whole of what can be promised: N configured keys cost N comparisons, and the plan says
    /// the number of administrators is not a secret — what must reveal nothing is whether
    /// administration is enabled here at all.</para>
    /// <para>The stand-in cannot be presented as a credential: matching it would need a key whose
    /// hash is those sixty-four characters, which is a preimage of SHA-256. A test asserts it is
    /// refused anyway, because a constant is a thing somebody can replace with a derivable one.</para>
    /// </remarks>
    internal IReadOnlyList<byte[]> Probed => _hashes.Length > 0 ? _hashes : Absent;

    /// <summary>The stand-in an unconfigured server compares against. Sixty-four hex characters, as a hash is.</summary>
    private static readonly byte[][] Absent = [Encoding.UTF8.GetBytes(new string('0', 64))];

    /// <summary>The configured hashes, for the ONE caller that has to compare them with the database.</summary>
    /// <remarks>
    /// The hashes, never the keys — there is no copy of a key here to hand out. It exists for the
    /// startup check that refuses a credential which is both an administrator and a contributor key:
    /// that check needs OUR hashes and the `api_keys` table in one place, and it is a configuration
    /// question asked once at startup rather than a credential comparison, so nothing here is
    /// timing-sensitive. Empty when nothing is configured — unlike <see cref="Probed"/>, which
    /// substitutes a stand-in and must never be mistaken for a list of real administrators.
    /// </remarks>
    internal IReadOnlyList<string> Hashes => [.. _hashes.Select(Encoding.UTF8.GetString)];

    /// <summary>The usable lines of the variable: blank ones and <c>#</c> comments dropped.</summary>
    private static IEnumerable<string> Lines(string? raw) =>
        (raw ?? string.Empty)
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'));

    /// <summary>What a presented credential came to.</summary>
    public abstract record Presented
    {
        private Presented()
        {
        }

        /// <summary>No configured administrator holds this key — or none is configured at all.</summary>
        /// <remarks>
        /// The two are ONE answer on purpose. A caller must not be able to tell "there are no
        /// administrators" from "you are not one of them": the first would make this endpoint an
        /// oracle for whether administration is enabled, answerable by anybody. The operator learns
        /// it from the startup log, which is on the host.
        /// </remarks>
        public sealed record Unknown : Presented;

        /// <summary>A configured administrator, by the hash their id derives from.</summary>
        public sealed record Administrator(string KeyHash) : Presented
        {
            /// <summary>The id the audit and the limiter use.</summary>
            public AdminId Id => AdminId.Of(KeyHash);
        }
    }
}
