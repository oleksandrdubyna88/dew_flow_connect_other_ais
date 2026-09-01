namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Failures a vendor CLI reports in its own words, translated into what to DO about them.
/// </summary>
/// <remarks>
/// <para>A stack trace is evidence; it is not a diagnosis. The Gemini CLI's retirement cost three
/// people a day between them: it was read as a daily quota, as a timeout and as an untrusted
/// working directory, by three different observers, because what it actually says is buried in a
/// node stack and phrased as an invitation rather than an error. When a vendor tells us plainly
/// that a door is closed, the gate should say so plainly too.</para>
/// <para>Pure and table-driven, so adding the next one is a row and a test.</para>
/// </remarks>
public static class VendorDiagnosis
{
    private static readonly (string Marker, string Sentence)[] Known =
    [
        // Google retired Gemini Code Assist for individuals on 2026-08-31. The CLI fails inside
        // `_doSetupUser`, BEFORE any model is reached, so it looks like anything you please.
        ("migrate to the Antigravity suite",
            "Google has retired Gemini Code Assist for individuals — this CLI can no longer review. " +
            "Install the Antigravity CLI and switch this vendor's runtime to 'antigravity'."),
        ("throwIneligibleOrProjectIdError",
            "the Gemini CLI reports this account as ineligible for its Code Assist tier — " +
            "switch this vendor's runtime to 'antigravity', which is Google's replacement."),
        ("not running in a trusted directory",
            "the CLI refused an untrusted working directory — a review runs in a fresh worktree, " +
            "so it needs --skip-trust or GEMINI_CLI_TRUST_WORKSPACE=true."),
        ("Missing optional dependency",
            "the CLI is installed without the binary for this platform — reinstall it " +
            "(npm install -g <the package>) and run it once by hand."),
        ("command not found",
            "the CLI is not on this machine's PATH — set an explicit executable path for this vendor."),
    ];

    /// <summary>
    /// A cure for what the vendor said, or nothing when this is not a failure we recognise.
    /// </summary>
    public static string? For(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : Known.FirstOrDefault(k => text.Contains(k.Marker, StringComparison.OrdinalIgnoreCase)).Sentence;

    /// <summary>
    /// A runtime this product knows is CLOSED, whatever its binary says when asked its version.
    /// </summary>
    /// <remarks>
    /// <c>gemini --version</c> exits 0: it prints a version without ever reaching Google, and the
    /// retirement only surfaces at sign-in. So the health probe — built on <c>--version</c> — was
    /// structurally incapable of seeing it, and reported "own auth, the CLI's own sign-in is used"
    /// for a vendor that could not sign in at all. Green health on a dead vendor is worse than no
    /// health at all: it is the reason a round was still being spent on it a day later.
    /// </remarks>
    public static string? ForRuntime(string runtime) => runtime?.Trim().ToLowerInvariant() switch
    {
        "gemini" => "RETIRED by Google for individual accounts: this CLI refuses before it reaches " +
                    "a model. Switch this vendor's runtime to 'antigravity'.",
        _ => null,
    };
}
