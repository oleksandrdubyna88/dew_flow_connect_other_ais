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
        // Measured in WSL 2026-09-01: a freshly installed codex answers a review with five reconnect
        // attempts and two 401s, and nothing in that wall says the one thing to do. The CLI is
        // there, the binary runs, the account simply has no session in THIS home directory — which
        // is the normal state of a second machine, a container, or a WSL distro beside a signed-in
        // Windows install.
        ("Missing bearer or basic authentication",
            "the CLI is installed but not signed in on this machine — run its login once " +
            "(`codex login`, `claude`, `gemini`) in the same user account the server runs as."),
        ("401 Unauthorized",
            "the vendor refused the credentials: the CLI is installed but not signed in here — " +
            "run its login once, or set this vendor's key in the vault entry."),
        ("has not been trusted",
            "the CLI refuses a directory nobody has accepted a trust dialog for, and a review runs " +
            "in a fresh worktree every round — accept it once interactively, or set this project's " +
            "trust flag in the CLI's own config."),
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
    /// <param name="linux">
    /// Whether this machine is Linux. It changes the answer for exactly one runtime: Antigravity
    /// works on Windows and has no CLI to install on Linux at all.
    /// </param>
    public static string? ForRuntime(string runtime, bool? linux = null) =>
        runtime?.Trim().ToLowerInvariant() switch
        {
            "gemini" => "RETIRED by Google for individual accounts: this CLI refuses before it reaches " +
                        "a model. Switch this vendor's runtime to 'antigravity'.",
            // Measured 2026-09-01: `agy` ships as a Go binary with the Antigravity app, npm has no
            // package for it, and the only Linux package is a third-party repackaging. So on Linux
            // this vendor cannot work, and "'agy' was not found on this machine" sends somebody
            // hunting for an install that does not exist. Naming the fact costs one sentence.
            "antigravity" when linux ?? OperatingSystem.IsLinux() =>
                "Antigravity has no Linux CLI that Google publishes — `agy` ships with the Antigravity " +
                "app, and npm has no package for it. On Linux use codex or claude as this reviewer, " +
                "or point this vendor's CLI path at a Windows agy.exe if you are on WSL.",
            _ => null,
        };
}
