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
        if (presented.Length == 0 || None)
        {
            return new Presented.Unknown();
        }

        var wanted = Encoding.UTF8.GetBytes(Corpus.HashOf(presented, secret));
        var found = string.Empty;
        foreach (var hash in _hashes)
        {
            if (CryptographicOperations.FixedTimeEquals(hash, wanted))
            {
                found = Corpus.HashOf(presented, secret);
            }
        }

        return found.Length == 0 ? new Presented.Unknown() : new Presented.Administrator(found);
    }

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
