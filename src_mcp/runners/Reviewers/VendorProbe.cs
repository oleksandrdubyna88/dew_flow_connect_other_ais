using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What one vendor looks like before anybody trusts it with a round.
/// </summary>
/// <param name="CliFound">
/// Whether the executable could be STARTED — not whether it works. A CLI that started and then
/// never answered is `true` here with a note saying so, because the two failures have different
/// cures: one is a missing install, the other a machine or a vendor that is busy.
/// </param>
/// <param name="Version">What the CLI said about itself, or empty.</param>
/// <param name="Auth">`own auth` · `vault key` · `unavailable` — the last of which bars the round.</param>
/// <param name="Note">
/// One sentence for a person: the cure when there is one, the vendor's own words otherwise. ONE,
/// deliberately — the panel renders a single note per row and `providers` answers a single note, so
/// a second field would be dropped on the floor.
/// </param>
public sealed record VendorHealth(bool Enabled, bool CliFound, string Version, string Auth, string Note);

/// <summary>
/// The health probe behind `providers` and, later, the Team server's catalog: run the CLI's own
/// `--version` and read the answer through the doors <see cref="VendorDiagnosis"/> knows about.
/// </summary>
/// <remarks>
/// Moved out of the MCP server so the second binary asks the same question rather than a similar
/// one. The arms are in the order they were in, and the order is the whole thing: a retired runtime
/// is answered BEFORE the probe, because `gemini --version` exits 0 without ever reaching Google.
/// </remarks>
public static class VendorProbe
{
    /// <summary>
    /// How long a CLI has to say its own version.
    /// </summary>
    /// <remarks>
    /// Thirty seconds, the value this probe has always used, now a named default rather than a
    /// literal — the plan round asked what bounds a hung CLI, and the answer should be visible in
    /// the signature rather than buried in a request. A probe is not a review: a CLI that cannot
    /// answer `--version` in half a minute is a finding, not a slow model.
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task<VendorHealth> RunAsync(
        IProcessLauncher launcher,
        VendorIdentity vendor,
        bool enabled,
        string executablePath,
        string model,
        bool hasVaultKey,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        // Resolved once: `For` constructs a runtime, and asking it twice per probe allocated one
        // per configured vendor per catalog call for nothing.
        var runtime = RuntimeResolution.For(vendor);
        if (runtime is null)
        {
            return new VendorHealth(enabled, false, "", "unavailable",
                ReviewerRuntimeSelector.Default.RefusalFor(vendor.Provider));
        }

        // A retired runtime is answered before the probe, not by it: `gemini --version` exits 0
        // without ever reaching Google, so a probe built on --version is structurally incapable of
        // seeing the retirement and reported "own auth" for a vendor that could not sign in at all.
        if (VendorDiagnosis.ForRuntime(RuntimeResolution.NameOf(vendor)) is { } retired)
        {
            return new VendorHealth(enabled, false, "", "unavailable", retired);
        }

        if (RuntimeResolution.NameOf(vendor) == "local")
        {
            return LocalHealth(vendor, enabled, model);
        }

        var (auth, authNote) = RuntimeResolution.AuthOf(vendor, hasVaultKey);
        if (!enabled)
        {
            return new VendorHealth(false, false, "", auth, "disabled in settings");
        }

        var exe = executablePath.Length > 0 ? executablePath : runtime.DefaultExecutable;

        return await AskItsVersionAsync(launcher, vendor, exe, auth, authNote, timeout ?? DefaultTimeout, ct);
    }

    /// <summary>
    /// A local engine is not a CLI: there is nothing to run `--version` on, and the version that
    /// matters is the ENGINE's, which the panel already probes over HTTP.
    /// </summary>
    /// <remarks>
    /// An endpoint cannot know which model nobody named, so the probe reads the configuration as
    /// well. Without this the vendor reported "own auth" and every reviewer it ran answered
    /// `400 model is required` — three of nine in a code round, gone, with the health probe saying
    /// the vendor was fine. Found by this repository's own bench on its first real campaign.
    /// </remarks>
    private static VendorHealth LocalHealth(VendorIdentity vendor, bool enabled, string model)
    {
        var endpoint = vendor.BaseUrl.Length > 0 ? vendor.BaseUrl : LocalRuntime.DefaultEndpoint;

        return model.Length == 0
            ? new VendorHealth(enabled, true, "", "unavailable",
                $"a local engine at {endpoint} with no model — name one in this vendor's Model "
                    + "field, or the engine answers 400 to every reviewer")
            : new VendorHealth(enabled, true, "", "own auth",
                $"a local engine at {endpoint} — no CLI, no key, no bill");
    }

    private static async Task<VendorHealth> AskItsVersionAsync(
        IProcessLauncher launcher,
        VendorIdentity vendor,
        string exe,
        string auth,
        string authNote,
        TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(
                new ProcessRequest(exe, ["--version"], Environment.CurrentDirectory) { Timeout = timeout },
                ct);

            // A CLI that never answers is its own diagnosis. It used to fall through to the
            // exit-code sentence below and report "--version exited -1", which names a number
            // nobody can act on for a process that was killed rather than one that refused.
            if (result.TimedOut)
            {
                return new VendorHealth(true, true, "", auth,
                    $"'{exe}' did not answer --version within {timeout.TotalSeconds:F0}s");
            }

            if (result.ExitCode == 0)
            {
                return new VendorHealth(true, true, result.StdOut.Trim(), auth, authNote);
            }

            // `providers` is the health probe a person reads before trusting a panel, so a known
            // closed door is named here too rather than only when a round has already failed.
            var cure = VendorDiagnosis.For(result.StdErr + result.StdOut);
            // Whichever stream the CLI actually used. Reading stderr alone left the note as
            // "--version exited 1: " for every vendor that writes its diagnosis to stdout — an exit
            // code and a colon, which is the shape of a sentence that helps nobody.
            var said = result.StdErr.Trim().Length > 0 ? result.StdErr.Trim() : result.StdOut.Trim();

            return new VendorHealth(true, true, "", auth,
                cure ?? $"--version exited {result.ExitCode}: {said}");
        }
        // Win32Exception is what a missing or unrunnable executable throws, and it is the case with
        // a cure. The other two are the ones a bad path can still produce — a directory where a file
        // was named, a handle that will not open — and a health probe that FAULTS takes the whole
        // catalog answer with it, which is a worse answer than any note.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // A missing CLI is the one failure with a one-line answer, so the answer goes here
            // rather than on a vendor's docs page. This used to be a blanket "antigravity has no
            // Linux CLI" door in VendorDiagnosis.ForRuntime, which fired BEFORE this probe and so
            // told a machine with a working `agy` that it had none.
            var install = VendorDiagnosis.InstallCure(RuntimeResolution.NameOf(vendor));

            return new VendorHealth(true, false, "", auth,
                install is null
                    ? $"'{exe}' was not found on this machine"
                    : $"'{exe}' was not found on this machine — {install}");
        }
    }
}
