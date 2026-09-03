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
    /// <param name="wsl">
    /// Whether this machine is a WSL distro, for the one cure that differs. Defaults to asking the
    /// kernel; the tests pass it, because a cure must be checkable on a machine that is not the one
    /// it is about.
    /// </param>
    public static string? For(string text, bool? wsl = null) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : UnreachableLocalEngine(text, wsl ?? UnderWsl.Value)
              ?? Known.FirstOrDefault(k => text.Contains(k.Marker, StringComparison.OrdinalIgnoreCase)).Sentence;

    /// <summary>
    /// The local engine refused the connection — what to do about it, on this kind of machine.
    /// </summary>
    /// <remarks>
    /// <para>Keyed on this product's OWN sentence rather than on the word "refused", which appears
    /// in every vendor's stack traces: a marker that fires on anything is a cure nobody can trust.
    /// The address is carried through into the cure, because it was the useful half of the original
    /// message and <c>Because</c> replaces the message with whatever comes back from here.</para>
    /// <para>Measured 2026-09-03 on one machine with coai 12.1 on both sides: from Windows the local
    /// reviewer answered, from WSL ten rounds in a row died in zero seconds against a Windows Ollama
    /// with fifteen models on it. Two barriers, and a person needs to be told about both — the
    /// engine binds <c>127.0.0.1</c>, and a distro's <c>127.0.0.1</c> is not the Windows host's.
    /// Mirrored networking removes both at once, which is why it is named first.</para>
    /// </remarks>
    private static string? UnreachableLocalEngine(string text, bool wsl)
    {
        const string opening = "the local engine at ";
        const string closing = " could not be reached";
        var start = text.IndexOf(opening, StringComparison.OrdinalIgnoreCase);
        var end = text.IndexOf(closing, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end <= start)
        {
            return null;
        }

        var endpoint = text[(start + opening.Length)..end].Trim();

        return wsl
            ? $"nothing is listening at {endpoint} on THIS side: a WSL distro's own 127.0.0.1 is not the "
              + "Windows host's, and a Windows engine binds loopback only. Switch WSL to mirrored "
              + "networking ([wsl2] networkingMode=mirrored in %USERPROFILE%\\.wslconfig, then "
              + "`wsl --shutdown`), or start the engine with OLLAMA_HOST=0.0.0.0 and set this vendor's "
              + "endpoint to the Windows host."
            : $"nothing is listening at {endpoint} — start the local engine, or set this vendor's "
              + "endpoint to where it actually runs.";
    }

    /// <summary>Whether this kernel is WSL, asked once.</summary>
    private static readonly Lazy<bool> UnderWsl = new(() =>
    {
        try
        {
            return OperatingSystem.IsLinux()
                   && File.Exists("/proc/version")
                   && NamesWsl(File.ReadAllText("/proc/version"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    });

    /// <summary>
    /// Whether a <c>/proc/version</c> banner is WSL's.
    /// </summary>
    /// <remarks>
    /// The distinction is not "am I on Linux", and getting that wrong was a finding on the plan: a
    /// native Linux box told to edit <c>%USERPROFILE%\.wslconfig</c> and run <c>wsl --shutdown</c>
    /// has been handed instructions for a machine it is not.
    /// </remarks>
    internal static bool NamesWsl(string procVersion) =>
        procVersion.Contains("microsoft", StringComparison.OrdinalIgnoreCase)
        || procVersion.Contains("WSL", StringComparison.OrdinalIgnoreCase);

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
    /// Kept for the callers that state their platform. NOTHING depends on it any more, and that is
    /// the fix: Antigravity had a blanket Linux door here, and a door in this method fires BEFORE
    /// the probe — so a machine with a working `agy` was told it had no CLI. This method answers one
    /// question only: is the runtime itself closed, whatever its binary says. Gemini is. A runtime
    /// whose CLI is merely absent is the probe's business, and <see cref="InstallCure"/> is what it
    /// says about it.
    /// </param>
    public static string? ForRuntime(string runtime, bool? linux = null) =>
        runtime?.Trim().ToLowerInvariant() switch
        {
            "gemini" => "RETIRED by Google for individual accounts: this CLI refuses before it reaches " +
                        "a model. Switch this vendor's runtime to 'antigravity'.",
            _ => null,
        };

    /// <summary>
    /// How to install a runtime whose CLI is not on this machine — the vendor's own published
    /// command, for this platform.
    /// </summary>
    /// <remarks>
    /// <para>"'agy' was not found on this machine" is a true sentence that leaves somebody searching
    /// a vendor's docs, which is the reason a reviewer never gets added. The command is one line and
    /// it is knowable here.</para>
    /// <para>Only official sources. Antigravity on Linux and macOS is Google's own script — verified
    /// 2026-09-01 to handle both, after this product had spent a day claiming no such thing existed.
    /// The snap package is a third-party repackaging and is deliberately not offered.</para>
    /// <para>What is NOT offered either: pointing a Linux server at a Windows <c>agy.exe</c> through
    /// WSL interop. It was measured — the process exits 1 after 60 seconds with "authentication
    /// timed out", because the Windows binary cannot reach a credential store from the Linux side.
    /// Advice that has been refuted is worse than none.</para>
    /// </remarks>
    public static string? InstallCure(string runtime, bool? linux = null) =>
        (runtime?.Trim().ToLowerInvariant(), linux ?? OperatingSystem.IsLinux()) switch
        {
            ("antigravity", true) =>
                "install Google's own CLI: curl -fsSL https://antigravity.google/cli/install.sh | bash",
            ("antigravity", false) =>
                "install Google's own CLI: irm https://antigravity.google/cli/install.ps1 | iex",
            ("codex", _) => "install it with: npm install -g @openai/codex",
            ("claude", _) => "install it with: npm install -g @anthropic-ai/claude-code",
            _ => null,
        };
}
