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
public readonly record struct VendorIdentity(string Provider, string Runtime, string BaseUrl);

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
        : vendor.BaseUrl.Length > 0 ? "codex"
        : vendor.Runtime.Length > 0 ? vendor.Runtime
        : vendor.Provider;

    /// <summary>
    /// The runtime for one configured reviewer: a built-in by name, or — when the operator gave it a
    /// base URL — the generic custom one. A vendor added in the panel is DATA, not a release.
    /// </summary>
    public static IReviewerRuntime? For(VendorIdentity vendor) =>
        // Through NameOf, which is the ONE place that answers "what is this vendor". It used to ask
        // `Runtime == "local"` here as well, and a third copy of the same question in the auth
        // decision was never updated — so a local reviewer was silently dropped from every round.
        NameOf(vendor) == "local"
            ? new LocalRuntime(vendor.Provider, vendor.BaseUrl)
            : vendor.BaseUrl.Length > 0
                ? new CustomCodexRuntime(vendor.Provider, vendor.BaseUrl)
                // An EXPLICIT runtime outranks the id, and that order is the fix for a real defect:
                // the id was consulted first, so a vendor called `claude` worked by accident while
                // `my-claude` — same runtime, different name — silently ran the Codex CLI. The
                // vendor's own id travels with the runtime; see ReviewerRuntimeSelector.Named for
                // what happened when it did not.
                : ReviewerRuntimeSelector.Named(vendor.Runtime, vendor.Provider)
                  ?? ReviewerRuntimeSelector.Default.Find(vendor.Provider);

    /// <summary>
    /// How a vendor authenticates — and therefore whether it can run at all.
    /// </summary>
    /// <remarks>
    /// An "unavailable" answer REMOVES the vendor from the round, so this is not a label for a
    /// panel. It decides who reviews.
    /// </remarks>
    public static (string Auth, string Note) AuthOf(VendorIdentity vendor, bool hasVaultKey) =>
        hasVaultKey
            ? ("vault key", "")
            : NameOf(vendor) == "local"
                ? ("own auth", "a local engine needs no key — it is reached over HTTP on this machine")
                : vendor.BaseUrl.Length > 0 || vendor.Provider is "deepseek"
                    ? ("unavailable", $"needs a key under '{vendor.Provider}' and the vault holds none — see the creds config entry")
                    : ("own auth", "the CLI's own sign-in is used");
}
