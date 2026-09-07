namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// One configured vendor, as the questions below need it: what it is called, which runtime it was
/// told to drive, and the endpoint it was given.
/// </summary>
/// <remarks>
/// Three fields and no more, because three is what the answers read. A model and an executable path
/// belong to a LAUNCH — <see cref="ReviewerSettings"/> carries those to <c>Build</c> — and widening
/// an identity past what its consumers use is how a type starts meaning two things.
/// </remarks>
/// <param name="RemoteVendor">
/// For a <c>remote</c> vendor: the id its TEAM SERVER knows it by, which is not this row's id — a row
/// is <c>&lt;server&gt;-&lt;vendor&gt;</c> so two servers offering the same vendor do not collide.
/// Empty falls back to <paramref name="Provider"/>, which is also what every non-remote vendor uses.
/// </param>
public readonly record struct VendorIdentity(
    string Provider, string Runtime, string BaseUrl, string RemoteVendor = "")
{
    /// <summary>The name to send to a Team server, or to look up in its catalog.</summary>
    public string VendorOnServer => RemoteVendor.Length > 0 ? RemoteVendor : Provider;

    /// <summary>
    /// Whether the ROW records its server's own name, or <see cref="VendorOnServer"/> is falling
    /// back to the id.
    /// </summary>
    /// <remarks>
    /// The fallback is right for a hand-written row somebody called <c>claude</c> and misleading for
    /// every row the panel generated, whose id is <c>&lt;server&gt;-&lt;vendor&gt;</c>. A server asked
    /// for <c>remsoftdev-claude</c> answers, truthfully, that it offers no such vendor — and that
    /// sentence sends a person hunting for a typo in a name they never typed. Whoever reports the
    /// refusal needs to know which of the two names it was.
    /// </remarks>
    public bool NamesItsServerVendor => RemoteVendor.Length > 0;
}

/// <summary>
/// What a configured vendor IS: which runtime it drives, the adapter for it, and how it
/// authenticates — which is also whether it can run at all.
/// </summary>
/// <remarks>
/// <para><b>One declaration, in the library, because this question has already been answered in two
/// places twice and a copy was missed both times.</b> `local` was added to the extension's type and
/// not to the list beside it, so every saved local reviewer came back as a codex one — under its own
/// name, listing codex's models. The server had the same omission in its own hand-written set, so a
/// local vendor became a codex vendor with a base URL, failed the key check that base URLs imply and
/// was dropped from every round while the panel reported it as configured. The docstring left behind
/// says it plainly: three copies of one decision is what allowed two of them to be right.</para>
/// <para>A second binary — the Team server — asks all three of these questions. Asking them from
/// here is what stops it becoming the fourth copy.</para>
/// <para>Pure and static: the decision is a unit test rather than a live round, and the round that
/// would have caught the defect above needs a model, a machine and four minutes.</para>
/// </remarks>
public static class RuntimeResolution
{
    /// <summary>Which runtime a vendor actually drives, by the same order the launcher uses.</summary>
    /// <remarks>
    /// `local` is checked FIRST, and that order is load-bearing: a local vendor IS a vendor with a
    /// base url, and the base-url arm means "ride the Codex CLI".
    /// </remarks>
    public static string NameOf(VendorIdentity vendor) =>
        vendor.Runtime == "local" ? "local"
        // `remote` is decided HERE, before the base-URL arm, for the same reason `local` is: a remote
        // row HAS a base URL — it is the Team server's — so falling through would classify every
        // Team server vendor as a custom codex endpoint. This file's own remarks below predicted
        // exactly that split before the name existed.
        : vendor.Runtime == "remote" ? "remote"
        : vendor.BaseUrl.Length > 0 ? "codex"
        : vendor.Runtime.Length > 0 ? vendor.Runtime
        : vendor.Provider;

    /// <summary>
    /// The runtime for one configured reviewer: a built-in by name, or — when the operator gave it a
    /// base URL — the generic custom one. A vendor added in the panel is DATA, not a release.
    /// </summary>
    /// <remarks>
    /// <para><b>It dispatches on <see cref="NameOf"/> and never re-decides anything for itself.</b>
    /// It used to ask `BaseUrl.Length > 0` again on its own, which read as harmless — the two agreed
    /// on every vendor that exists today. They would have stopped agreeing on the first new name:
    /// `NameOf` would answer `remote` while this handed back the Codex adapter, which is the split
    /// decision this whole type was extracted to end, reintroduced inside it. Raised by codex and
    /// gemini on this change's code round; <c>TheAdapterAVendorGets_IsTheRuntimeItWasNamed</c> is
    /// the guard.</para>
    /// <para>The base-URL arm survives as a CONDITION on the codex name rather than a decision of
    /// its own: `NameOf` has already turned "has an endpoint" into `codex`, and what is left here is
    /// which codex — the CLI's own service, or somebody else's.</para>
    /// </remarks>
    public static IReviewerRuntime? For(VendorIdentity vendor) => NameOf(vendor) switch
    {
        "local" => new LocalRuntime(vendor.Provider, vendor.BaseUrl),
        "remote" => new RemoteRuntime(vendor.Provider, vendor.BaseUrl, vendor.VendorOnServer),
        "codex" when vendor.BaseUrl.Length > 0 => new CustomCodexRuntime(vendor.Provider, vendor.BaseUrl),
        // An EXPLICIT runtime outranks the id, and that order is the fix for a real defect: the id
        // was consulted first, so a vendor called `claude` worked by accident while `my-claude` —
        // same runtime, different name — silently ran the Codex CLI. The vendor's own id travels
        // with the runtime; see ReviewerRuntimeSelector.Named for what happened when it did not.
        var name => ReviewerRuntimeSelector.Named(name, vendor.Provider)
                    ?? ReviewerRuntimeSelector.Default.Find(vendor.Provider),
    };

    /// <summary>
    /// How a vendor authenticates — and therefore whether it can run at all.
    /// </summary>
    /// <remarks>
    /// An "unavailable" answer REMOVES the vendor from the round, so this is not a label for a
    /// panel. It decides who reviews.
    /// </remarks>
    /// <param name="hasServerToken">
    /// Whether this machine has signed into the Team server this vendor points at. Only consulted
    /// for a <c>remote</c> vendor, and passed in rather than read here so this stays pure.
    /// </param>
    public static (string Auth, string Note) AuthOf(
        VendorIdentity vendor, bool hasVaultKey, bool hasServerToken = false) =>
        // `remote` is decided BEFORE the vault key, because a Team server's authentication is not
        // a key at all — it is a session this machine holds. Saying "vault key" for a vendor that
        // uses none would send somebody to configure the wrong thing entirely.
        NameOf(vendor) == "remote" ? TeamServerAuthOf(vendor, hasServerToken)
        : hasVaultKey
            ? ("vault key", "")
            : NameOf(vendor) == "local"
                ? ("own auth", "a local engine needs no key — it is reached over HTTP on this machine")
                : vendor.BaseUrl.Length > 0 || vendor.Provider is "deepseek"
                    ? ("unavailable", $"needs a key under '{vendor.Provider}' and the vault holds none — see the creds config entry")
                    : ("own auth", "the CLI's own sign-in is used");

    /// <summary>
    /// A Team server vendor authenticates with the SESSION this machine holds, not with a key.
    /// </summary>
    /// <remarks>
    /// Extracted so <see cref="AuthOf"/> stays readable, and so the "not signed in" sentence has ONE
    /// author: <see cref="RemoteAsk.NotSignedInMessage"/> already says it to the person whose review
    /// just refused to run, and a hand-written second copy here would drift from it the first time
    /// either was reworded.
    /// </remarks>
    private static (string Auth, string Note) TeamServerAuthOf(VendorIdentity vendor, bool hasServerToken) =>
        hasServerToken
            ? ("server token", $"signed in to the Team server at {TeamServerAuth.Normalise(vendor.BaseUrl)}")
            : ("unavailable", RemoteAsk.NotSignedInMessage(TeamServerAuth.Normalise(vendor.BaseUrl)));
}
