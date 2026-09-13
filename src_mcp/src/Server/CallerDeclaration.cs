using System.Globalization;
namespace CoaiMcp.Server;

/// <summary>
/// Which AI is calling, and which model it says it is running.
/// </summary>
/// <remarks>
/// <para><b>Two sources, because the protocol only has one of them.</b> MCP's <c>initialize</c>
/// carries <c>clientInfo { name, version }</c> — <c>claude-code</c>, <c>codex</c>, <c>gemini-cli</c>
/// — and no field of the protocol carries a MODEL. So the client comes from the handshake and the
/// model is DECLARED by the calling AI on <c>open</c>. Decided by the operator on 2026-09-13.</para>
///
/// <para><b>Why not an environment variable for the model.</b> <c>COAI_CALLER_MODEL</c> beside the
/// existing <c>COAI_CALLER_SESSION</c> would be cheaper and need no argument — but it is set when
/// the client STARTS. Switch model mid-session with <c>/model</c> and the log confidently names the
/// model you stopped using, and a confidently wrong log is worse than a silent one. A declaration
/// tracks the switch because it is sent per <c>open</c>.</para>
///
/// <para><b>Why the handshake outranks the variables for the vendor.</b> The variables are the
/// PROCESS's, inherited from whatever launched this server, and they never change while it runs.
/// The handshake is negotiated on the connection that is calling. A server whose environment says
/// claude and whose caller is codex would otherwise record the launcher for ever — raised as
/// Blocking by gemini on this change's plan round.</para>
///
/// <para><b>Nothing here is ever guessed.</b> A caller that declares no model is recorded as
/// declaring none and reads as "model not stated"; a caller nothing identifies is
/// <see cref="CallerIdentity.Unknown"/>. Both are states with a name, because the only alternative
/// is a blank that reads as "claude" to whoever looks at it.</para>
/// </remarks>
/// <param name="Vendor">The handshake's vendor, else the variable's, else unknown.</param>
/// <param name="Client">What the client called ITSELF, verbatim — empty when it said nothing.</param>
/// <param name="ClientVersion">That client's version from the same handshake.</param>
/// <param name="Model">What the calling AI declared on <c>open</c>. Empty means it declared none.</param>
public sealed record CallerDeclaration(
    string Vendor = CallerIdentity.Unknown,
    string Client = "",
    string ClientVersion = "",
    string Model = "")
{
    /// <summary>
    /// How much of somebody else's string is kept.
    /// </summary>
    /// <remarks>
    /// These three fields are freeform input from an external AI, written to a session file, a
    /// database column and a page. A cap is the difference between a field and an injection point;
    /// no real client name or model id comes near it. (gemini, Minor, on the plan round.)
    /// </remarks>
    public const int LongestField = 120;

    /// <summary>What the declaration says, as one phrase a person can read.</summary>
    /// <remarks>
    /// Built here rather than on the page so both halves say the same thing, and so "not stated" is
    /// impossible to render as an empty span by forgetting to.
    /// </remarks>
    public string Phrase =>
        $"{(Client.Length > 0 ? Client : Vendor)}{(ClientVersion.Length > 0 ? " " + ClientVersion : "")}"
        + $" · {(Model.Length > 0 ? Model : "model not stated")}";

    /// <summary>
    /// The declaration for one <c>open</c>: what the handshake said, and what the AI declared.
    /// </summary>
    /// <remarks>
    /// Every argument is somebody else's and may be null — <c>clientInfo</c> is optional in the
    /// protocol and the SDK's property is nullable — so a handshake that carried nothing must
    /// produce a record rather than an exception.
    /// </remarks>
    public static CallerDeclaration From(
        CallerIdentity identity, string? client, string? clientVersion, string? model)
    {
        var named = Field(client);
        return new CallerDeclaration(
            VendorFor(identity, named), named, Field(clientVersion), Field(model));
    }

    /// <summary>
    /// Whose client this is: the handshake's answer, the environment's, or nobody's.
    /// </summary>
    /// <remarks>
    /// <para><b>The environment is consulted only when the handshake said NOTHING.</b> Raised by
    /// codex on the second code round, and it is the rule of this whole change applied to its own
    /// fallback: a client that calls itself <c>some-editor</c> has identified itself, and it is not
    /// claude however the server was launched. Adopting the launcher's vendor there would be exactly
    /// the guess everything else here refuses to make — and a long-lived server serving a second
    /// client is the case that makes it wrong rather than merely untidy.</para>
    /// <para><c>Stated</c> is not a vendor and never becomes one. It records that the id came from
    /// <c>COAI_CALLER_SESSION</c> — an operator's string, which implies nothing about whose client
    /// this is — so as a VENDOR it resolves to <c>unknown</c>, which is the word for that state.
    /// Rendering "asked by stated" would read as a vendor called "stated". (gemini, same round.)</para>
    /// </remarks>
    private static string VendorFor(CallerIdentity identity, string client) =>
        client.Length > 0
            ? VendorOf(client) ?? CallerIdentity.Unknown
            : identity.Vendor is { Length: > 0 } and not CallerIdentity.Stated and { } known
                ? known
                : CallerIdentity.Unknown;

    /// <summary>
    /// The vendors a client name can be recognised as, or null for one we do not know.
    /// </summary>
    /// <remarks>
    /// An explicit list matched from the START of the name, not a substring search: <c>claude-code</c>
    /// is Anthropic's client and <c>gemini-cli</c> is Google's, while an editor that merely mentions
    /// a vendor somewhere in its name is not that vendor's client. A name off this list is kept
    /// verbatim as the client, and the vendor is <c>unknown</c> rather than borrowed.
    /// </remarks>
    private static string? VendorOf(string client) =>
        new[] { "claude", "codex", "gemini", "antigravity" }
            .FirstOrDefault(vendor => client.StartsWith(vendor, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One field of somebody else's: trimmed, stripped of invisible characters, and bounded.
    /// </summary>
    /// <remarks>
    /// <para>Control characters go because these strings end up in log lines and on a page, and a
    /// newline inside a log line is a second log line that nobody wrote. Whitespace-only is
    /// emptiness: a model of <c>"   "</c> is a model nobody declared, and recording it as a
    /// declaration would put a blank where "not stated" belongs.</para>
    /// <para><b>Unicode FORMAT characters go too, and <c>char.IsControl</c> does not cover them.</b>
    /// Raised by gemini on the code round: category <c>Cf</c> holds the bidirectional overrides
    /// (U+202E and friends) and the zero-width joiners, none of which is a control character by
    /// that predicate. A caller is an external AI and this string is rendered beside a vendor name
    /// in a security log — an override reverses everything after it, so <c>claude-opus-5</c> can be
    /// made to READ as something else entirely while the bytes say what they say. Spoofing an
    /// identity in the record of who reviewed what is worth one extra predicate.</para>
    /// <para>Bounded BEFORE the whole value is materialised: a caller that sends ten megabytes on
    /// every <c>open</c> would otherwise allocate all of it twice to keep 120 characters. (codex,
    /// same round.)</para>
    /// </remarks>
    private static string Field(string? value)
    {
        // Lazy the whole way, so ten megabytes of somebody else's string never becomes an array:
        // drop the invisible characters, drop the LEADING whitespace (before the cap, or a padded
        // value would spend its budget on spaces), keep one more than the cap so the trailing trim
        // still has something to remove, and only then materialise.
        var kept = new string((value ?? string.Empty)
            .Where(Keepable)
            .SkipWhile(char.IsWhiteSpace)
            .Take(LongestField + 1)
            .ToArray()).TrimEnd();

        return kept.Length > LongestField ? kept[..LongestField] : kept;
    }

    /// <summary>Everything that is not invisible: not a control character, not a format one.</summary>
    private static bool Keepable(char c) =>
        !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format;
}
