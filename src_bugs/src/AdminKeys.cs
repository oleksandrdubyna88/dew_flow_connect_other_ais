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

    /// <summary>
    /// The line every encoded list must begin with, and the reason the shapes are disjoint at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Without it they are not.</b> The first version of this rule refused whitespace,
    /// non-UTF-8 bytes and control characters, and argued that a raw key list could not survive all
    /// three. A code-round reviewer produced one that does: <c>aCE0</c> repeated sixteen times is a
    /// raw 64-character key, has no whitespace, is valid base64, and decodes to <c>h!4</c> repeated
    /// — printable, no control characters, perfectly good UTF-8. It would have been accepted as an
    /// administrator nobody holds, and the deployment's own check would have PASSED, because the
    /// workflow decodes the same value and would have sent the same nonsense key.</para>
    /// <para>So the encoded form carries a marker and the raw form cannot. It is spelled as a
    /// COMMENT so that every reader of the list already drops it: <see cref="Lines"/> ignores `#`
    /// lines, and so does the deployment when it picks a key to test with. Nothing had to learn a
    /// new rule except this method.</para>
    /// <para>It costs the operator one line in the command they paste, and it is safe to introduce
    /// today for a reason that will not come again: the admin surface has never shipped, so there is
    /// no deployed value to migrate.</para>
    /// </remarks>
    public const string Marker = "# coai-bugs-admin-keys v1";

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

    /// <summary>Reads the DELIVERED variable — base64 — or says why the server cannot start.</summary>
    /// <param name="delivered">The variable's value, or <c>null</c> when it is absent.</param>
    /// <param name="secret">The server secret, so a stolen database is not a rainbow-table exercise.</param>
    /// <remarks>
    /// <para>An absent variable and an empty one come to the same thing — no administrators — and
    /// that is not a startup failure. An admin API with no administrators answers 401 to everybody,
    /// which is a legitimate way to run this server and is the reason <see cref="None"/> is reported
    /// at startup rather than thrown.</para>
    /// <para><b>Anything else is BASE64, always.</b> See <see cref="Decode"/> for why there is no
    /// fallback to the raw list and why "it decodes" is not the whole test.</para>
    /// </remarks>
    public static Configured Read(string? delivered, string secret) =>
        string.IsNullOrEmpty(delivered)
            ? new Configured.Admins(Of(string.Empty, secret))
            : Decode(delivered, secret);

    /// <summary>The administrators a key list NAMES: one per line, blank lines and comments dropped.</summary>
    /// <remarks>
    /// The text, never the wire. <see cref="Read"/> is what the environment goes through; this is
    /// what the text inside it means, and it is separate because the two are different questions —
    /// one is a delivery format and one is the operator's list.
    /// </remarks>
    public static AdminKeys Of(string text, string secret) =>
        new([.. Lines(text).Select(key => Encoding.UTF8.GetBytes(Corpus.HashOf(key, secret)))]);

    /// <summary>
    /// The delivered value, decoded — or the reason this server will not start with it.
    /// </summary>
    /// <remarks>
    /// <para><b>There is no fallback to the raw list, deliberately.</b> A raw list and an encoded one
    /// are both non-empty text, so a server that tried base64 and fell back would turn a typo into a
    /// server running with the WRONG administrators — configured, and wrong, with nothing in the
    /// startup log saying so. An unusable value exits 78 naming the variable, like every other
    /// setting here.</para>
    /// <para><b>And "it is valid base64" is not the whole test, which is the part that is easy to
    /// miss.</b> The two shapes genuinely overlap: a 64-character hex key is inside the base64
    /// alphabet and its length is a multiple of four, and <see cref="Convert.TryFromBase64String"/>
    /// IGNORES whitespace — so a raw two-line list can decode, silently, to nonsense. Three rules
    /// make them disjoint: no whitespace in the encoded value (`base64 -w0` produces none, a raw
    /// list always has some), strict UTF-8 on the bytes, and no control characters in the text.
    /// (Plan round, all three reviewers.)</para>
    /// </remarks>
    private static Configured Decode(string delivered, string secret)
    {
        if (delivered.Any(char.IsWhiteSpace))
        {
            return Unusable("contains a space, a line break or a carriage return");
        }

        // Three base64 characters carry two bytes, so the encoded length is always enough room.
        var bytes = new byte[delivered.Length];

        return Convert.TryFromBase64String(delivered, bytes, out var written)
            ? Decoded(bytes.AsSpan(0, written), secret)
            : Unusable("is not base64");
    }

    /// <summary>The decoded bytes as the operator's list, or the reason they are not one.</summary>
    private static Configured Decoded(ReadOnlySpan<byte> bytes, string secret) =>
        Text(bytes, out var text) ? Marked(text, secret) : Unusable("does not decode to text");

    /// <summary>
    /// The decoded text, if it is an encoded list at all rather than something that decoded.
    /// </summary>
    /// <remarks>
    /// The byte-order mark gets its own sentence because it is invisible: a Windows editor writes
    /// one, the file looks exactly right, and every rule below would pass while the first key
    /// silently became BOM-plus-key and matched nothing.
    /// </remarks>
    private static Configured Marked(string text, string secret)
    {
        if (text.StartsWith('\uFEFF'))
        {
            return Unusable("begins with a byte-order mark, which no key has. Save the list as UTF-8 without a BOM");
        }

        var first = text.Split('\n', 2)[0].TrimEnd();

        return first == Marker
            ? new Configured.Admins(Of(text, secret))
            : Unusable($"does not begin with '{Marker}'");
    }

    /// <summary>
    /// Whether the bytes are text a person could have typed.
    /// </summary>
    /// <remarks>
    /// STRICT UTF-8 — the decoder throws rather than substituting replacement characters — and no
    /// control characters beyond the ones the format is made of. Between them they refuse a payload
    /// that decoded cleanly and means nothing, which is what a raw key list does when it happens to
    /// be valid base64: 48 bytes of binary nobody can present, configured as an administrator.
    /// </remarks>
    private static bool Text(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;

            return false;
        }

        return !text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    }

    /// <summary>A UTF-8 decoder that refuses rather than substituting. One instance; it is stateless.</summary>
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The one refusal sentence, which names what to edit and the command that produces it.
    /// </summary>
    /// <remarks>
    /// One sentence rather than one per rule: every one of these has the same fix, and an operator
    /// reading it at 02:00 needs the command more than they need to know which rule fired. The
    /// carriage return is named because this repository's operators work on Windows, where an editor
    /// adds one nobody can see. (Plan round, gemini.)
    /// </remarks>
    private static Configured Unusable(string what) =>
        new Configured.Refused(
            $"{Variable} {what}. It must be the key list encoded as base64 on ONE line — no spaces, "
            + "no line breaks, no carriage returns — and the list itself must begin with "
            + $"'{Marker}'. What to paste into the secret box:\n\n"
            + $"    printf '{Marker}\\n# alice\\n<alice-key>\\n' | base64 -w0\n\n"
            + "It is never the raw key list, and the marker is what makes those two things tell "
            + "apart: a raw key can be valid base64 that decodes to ordinary text, so without it a "
            + "server could start with administrators nobody holds and say nothing.");

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

    /// <summary>What the delivered variable came to: administrators, or the reason there are none.</summary>
    /// <remarks>Mirrors <c>RatePerMinute.Parsed</c>, which is how every other unusable setting here
    /// reaches <c>Startup.Refused</c> and exit 78.</remarks>
    public abstract record Configured
    {
        private Configured()
        {
        }

        /// <summary>A usable value — including one that names nobody.</summary>
        public sealed record Admins(AdminKeys Keys) : Configured;

        /// <summary>A value the server will not start with, and the sentence that says so.</summary>
        public sealed record Refused(string Why) : Configured;
    }

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
