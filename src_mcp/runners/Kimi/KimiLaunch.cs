using System.ComponentModel;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// Everything a Kimi launch is checked against before it starts — the ONE launch control every Kimi launch path goes through (D8b, D10,
/// D17, D18, D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>). The environment it
/// builds and admits is <see cref="KimiEnvironment"/>'s.
/// </summary>
public static class KimiLaunch
{
    /// <summary>How long the <c>--version</c> asked before every launch may take.</summary>
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>D10, before the launch: why this model is not launched on, or empty when it is curated.</summary>
    public static string ModelRefusal(string model) => KimiModels.RefusalFor(model, KimiModels.Curated);

    /// <summary>
    /// D18, lifted for WSL2 by DL1 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c> (E6) and for a native Linux host by the owner's decision of 2026-10-23 (<see cref="HostNote"/>): a Windows host starting a Windows <c>kimi</c>, a WSL2 host,
    /// or a native Linux host whose kernel banner was READ. WSL1 (a syscall translation layer), a Linux host whose banner could not be read (coai cannot tell it from WSL1) and every other host are refused, each with its own sentence;
    /// a WSL bridge named as the executable of a Windows host is the Linux side by another door. The very sentences Vibe's launch control
    /// answers (<see cref="HostAdmission"/>), with Kimi's name.
    /// </summary>
    public static string HostRefusal(HostKind host, string executable, WslGeneration wsl) => HostAdmission.Refusal("Kimi", host, executable, wsl);

    /// <summary>
    /// What the person should know about the HOST a launch runs on, or empty: on a native Linux host with a read banner, that Kimi is NOT MEASURED there and runs by the owner's decision of 2026-10-23 (DN1 of
    /// <c>todo/PLAN_kimi_and_vibe_on_native_linux_E2.md</c>) — a note, never a refusal. The same sentence on the launch (<see cref="LaunchTurn.Warning"/>) and on the row's card (<see cref="CardWarning"/>).
    /// </summary>
    public static string HostNote(KimiHost host) => HostAdmission.Note("Kimi", host.Kind, host.Wsl);

    /// <summary>The card's warning: the host note and the version warning (<see cref="VersionWarning"/>) — the two things the person should know before a round, on one line each side of a separator.</summary>
    public static string CardWarning(KimiHost host, string printedVersion) => Joined(HostNote(host), VersionWarning(printedVersion));

    /// <summary>The model the request names (<c>--model</c>).</summary>
    public static string ModelOf(ProcessRequest request) => KimiEnvironment.FlagOf(request, AcpWords.ModelFlag);

    /// <summary>
    /// The warning for a Kimi version nobody measured — the same sentence on the launch (<see cref="LaunchTurn.Warning"/>) and on the
    /// row's card — or empty for a measured one (or when nothing printed a version).
    /// </summary>
    public static string VersionWarning(string printed) => WarningFor(VersionWarnings.VersionIn(printed));

    private static string WarningFor(string version) =>
        version.Length == 0 || KimiAcpProfile.MeasuredVersions.Contains(version)
            ? string.Empty
            : VersionWarnings.Unmeasured(
                "Kimi", version, string.Join(", ", KimiAcpProfile.MeasuredVersions),
                $"every confinement number coai has — the write refusals, the owned home's inventory, the OS-home redirect (research/RESULTS_kimi_confinement.md) — is {string.Join(", ", KimiAcpProfile.MeasuredVersions)}'s",
                "re-run `node scripts/probe-kimi-confinement.mjs` (the confinement probe, a paid run) and `node scripts/probe-kimi-tree-hold.mjs` (the sign-in hold) on it");

    /// <summary>What the checks before the turn concluded: a refusal, or the config bytes the home must hold.</summary>
    private sealed record Prepared(string Refusal, byte[] Expected)
    {
        public static Prepared Refuse(string refusal) => new(refusal, []);
    }

    /// <summary>A test's way in with a bare kind of host — the kernel's generation is this machine's when the kind is WSL (<see cref="KimiHost.Of"/>).</summary>
    internal static Task<LaunchTurn> TakeAsync(HostKind host, ProcessRequest request, KimiPlaces places, IProcessLauncher launcher, CancellationToken ct) =>
        TakeAsync(KimiHost.Of(host), request, places, launcher, ct);

    /// <summary>
    /// The whole check, in order, ending holding the home's turn and the sign-in's gate — or a refusal with nothing held.
    /// </summary>
    /// <remarks>
    /// <para><b>Before the turn</b>, nothing is taken and nothing started: the host (D18), the tools, the model (D10), the built
    /// environment, the person's OAuth slot, their credentials folder, the model block, the rendered config, and the home made
    /// (E4.1's UNC, chained-link and fresh-path refusals come through <see cref="KimiHome.Made"/>). <b>Then</b> the home's
    /// in-process turn — narrow, cheap — and the sign-in's gate — wide — both held until the launch has been read, and given back in
    /// reverse on EVERY way out: a refusal, a throw, a cancellation while the gate or the version waits. <b>Inside both</b>: the
    /// home's shape, the installed version in the same built environment on the owned home, the shape AGAIN (the <c>--version</c>
    /// process is Kimi's own and runs on the home), and the verdict — an unmeasured version is a warning, never a refusal (the owner's
    /// decision of 2026-10-13).</para>
    /// <para>Never reads <c>SpecialFolder.UserProfile</c> or a location from the environment: every path arrives in
    /// <paramref name="places"/>, resolved once in the unredirected parent (decision 4). The one environment read is <see cref="KimiEnvironment.Built(ProcessRequest, KimiPlaces)"/>'s fallback to this process's <c>PATH</c> — a list to keep, never a place to find.</para>
    /// </remarks>
    public static async Task<LaunchTurn> TakeAsync(KimiHost host, ProcessRequest request, KimiPlaces places, IProcessLauncher launcher, CancellationToken ct, bool allowNetworkSignIn = false)
    {
        var prepared = Prepare(host, request, places, allowNetworkSignIn);
        if (prepared.Refusal.Length > 0)
        {
            return LaunchTurn.Refused(prepared.Refusal, RefusalCause.LaunchControl);
        }

        var turn = await VibeHomeTurns.TakeAsync(places.OwnedRoot, ct);
        var gate = await GateAsync(places, turn, ct);
        if (gate is not Hold.Taken taken)
        {
            turn.Dispose();

            return LaunchTurn.Refused(LockRefusal((Hold.Unusable)gate, places.Credentials), RefusalCause.LaunchControl);
        }

        var survivors = await SurvivorsAsync(places, taken, turn, ct);
        if (survivors.Length > 0)
        {
            return LaunchTurn.Refused(survivors, RefusalCause.LaunchControl);
        }

        return await InsideAsync(places, request, prepared.Expected, Joined(HostNote(host), NetworkWarning(host, places, allowNetworkSignIn)), new Attempt(turn, taken.Stream), launcher, ct);
    }

    /// <summary>
    /// The A2-A3 seam (<see cref="IKimiSurvivors"/>), asked with the gate held: a free lock is not a free sign-in while a survivor marker stands. The sentence when one does — both holds given back — or empty; a
    /// throw or a cancellation gives them back before it propagates.
    /// </summary>
    private static async Task<string> SurvivorsAsync(KimiPlaces places, Hold.Taken gate, IDisposable turn, CancellationToken ct)
    {
        try
        {
            var marker = KimiCredentialsLock.MarkerPathFor(places.Credentials);
            var standing = marker.Length == 0 ? KimiCredentialsLock.Unnamed(places.Credentials) : await KimiSurvivors.Gate.BlockedAsync(marker, ct);
            if (standing.Length > 0)
            {
                Given(gate, turn);
            }

            return standing;
        }
        catch
        {
            Given(gate, turn);
            throw;
        }
    }

    private static void Given(Hold.Taken gate, IDisposable turn)
    {
        gate.Stream.Dispose();
        turn.Dispose();
    }

    /// <summary>The gate, taken with the home's turn already held: a throw or a cancellation gives the turn back before it propagates.</summary>
    private static async Task<Hold> GateAsync(KimiPlaces places, IDisposable turn, CancellationToken ct)
    {
        try
        {
            return await KimiCredentialsLock.TakeAsync(places.Credentials, ct);
        }
        catch
        {
            turn.Dispose();
            throw;
        }
    }

    /// <summary>Inside both holds: any throw gives them back, the stream first and then the turn.</summary>
    private static async Task<LaunchTurn> InsideAsync(KimiPlaces places, ProcessRequest request, byte[] expected, string warning, Attempt attempt, IProcessLauncher launcher, CancellationToken ct)
    {
        try
        {
            return await VerdictAsync(places, request, expected, warning, attempt, launcher, ct);
        }
        catch
        {
            attempt.Dispose();
            throw;
        }
    }

    /// <summary>The shape, the version, the shape again — a refusal gives both holds back — then the go, with the version warning when it was not measured.</summary>
    private static async Task<LaunchTurn> VerdictAsync(KimiPlaces places, ProcessRequest request, byte[] expected, string warning, Attempt attempt, IProcessLauncher launcher, CancellationToken ct)
    {
        var shape = KimiHome.Refusal(places, expected);
        if (shape.Length > 0)
        {
            return Given(attempt, shape);
        }

        var asked = await VersionAsync(places, request, launcher, ct);
        var after = asked.Refusal.Length > 0 ? asked.Refusal : KimiHome.Refusal(places, expected);

        return after.Length > 0
            ? Given(attempt, after, asked.CliMissing && asked.Refusal.Length > 0 ? RefusalCause.CliMissing : RefusalCause.LaunchControl)
            : LaunchTurn.Holding(attempt, Joined(warning, WarningFor(asked.Version)), "kimi " + asked.Version);
    }

    /// <summary>
    /// What the person should know about a sign-in folder the override admitted on network storage (DL6, <c>coai.kimiAllowNetworkSignIn</c>) — one line, or empty (the override is off, or the folder is on a local disk).
    /// </summary>
    private static string NetworkWarning(KimiHost host, KimiPlaces places, bool allowNetworkSignIn) =>
        allowNetworkSignIn ? Junctions.NetworkWarning(host.Kind, places.Credentials) : string.Empty;

    /// <summary>The warnings that are there, in order, on one line each side of a separator.</summary>
    private static string Joined(string first, string second) =>
        string.Join("; ", new[] { first, second }.Where(warning => warning.Length > 0));

    private static LaunchTurn Given(IDisposable attempt, string refusal, RefusalCause cause = RefusalCause.LaunchControl)
    {
        attempt.Dispose();

        return LaunchTurn.Refused(refusal, cause);
    }

    /// <summary>
    /// The person's home must be on a local drive, judged by its SPELLING before anything is read or opened: a UNC or mapped-drive home would
    /// be connected to (NTLM, or the SMB timeout) by the very first read of its config, before `Junctions` refused it. With the network override on, a Linux or WSL host also admits the automount spellings
    /// (<see cref="Junctions.IsLocalSpelling(HostKind, string, bool)"/>) — the same decision the link's own checks make, so the two cannot disagree.
    /// </summary>
    private static string LocalHome(KimiHost host, KimiPlaces places, bool allowNetworkSignIn) =>
        Junctions.IsLocalSpelling(host.Kind, places.Credentials, allowNetworkSignIn)
            ? string.Empty
            : $"the Kimi sign-in folder '{places.Credentials}' is not on a local drive — coai reads and links the sign-in from a local drive only, and never connects out to a network or roaming location (D19)";

    /// <summary>Everything that can be said before any lock is taken, in order — the first refusal wins; the config bytes the home must hold otherwise.</summary>
    private static Prepared Prepare(KimiHost host, ProcessRequest request, KimiPlaces places, bool allowNetworkSignIn)
    {
        var first = FirstOf(
            () => HostRefusal(host.Kind, places.Executable, host.Wsl),
            PidfdLayer.Refusal,
            () => places.ToolsRefusal,
            () => LocalHome(host, places, allowNetworkSignIn),
            () => ModelRefusal(ModelOf(request)),
            () => KimiEnvironment.Refusal(request, places));
        if (first.Length > 0)
        {
            return Prepared.Refuse(first);
        }

        var slot = KimiPersonsConfig.ReadFile(places.PersonsConfig);

        return slot is SlotRead.Found found ? Rendered(host, ModelOf(request), places, found.Slot, allowNetworkSignIn) : Prepared.Refuse(((SlotRead.Refused)slot).Reason);
    }

    /// <summary>The slot is read: the credentials folder, the model block and the render, then the home made.</summary>
    private static Prepared Rendered(KimiHost host, string model, KimiPlaces places, KimiOauthSlot slot, bool allowNetworkSignIn)
    {
        if (!Directory.Exists(places.Credentials))
        {
            return Prepared.Refuse($"not signed in to Kimi Code — {places.PersonsHome} has no credentials folder; run `kimi login` in a terminal and try again");
        }

        // B of the review of 2026-10-18: where the folder IS is judged on EVERY launch, not only when the home is made — the override may have been switched off, or a mount may have changed under the same folder.
        var locality = Junctions.Locality(host.Kind, places.Credentials, allowNetworkSignIn);
        if (locality.Length > 0)
        {
            return Prepared.Refuse(locality);
        }

        var expected = ExpectedConfig(model, places, slot);
        if (expected.Refusal.Length > 0)
        {
            return expected;
        }

        var made = KimiHome.Made(host.Kind, places, expected.Expected, allowNetworkSignIn);

        return made.Length > 0 ? Prepared.Refuse(made) : expected;
    }

    private static Prepared ExpectedConfig(string model, KimiPlaces places, KimiOauthSlot slot)
    {
        if (KimiModelBlocks.Builtin is not BlocksLoad.Loaded loaded)
        {
            return Prepared.Refuse(((BlocksLoad.Broken)KimiModelBlocks.Builtin).Reason);
        }

        var block = KimiModelBlocks.For(model, ReadPersons(places.PersonsConfig), loaded.Blocks);

        return block is BlockRead.Found found ? Render(slot, found.Block) : Prepared.Refuse(((BlockRead.Refused)block).Reason);
    }

    private static Prepared Render(KimiOauthSlot slot, KimiModelBlock block)
    {
        try
        {
            return new Prepared(string.Empty, KimiConfig.Bytes(KimiConfig.Render(slot, block)));
        }
        catch (ArgumentException e)
        {
            return Prepared.Refuse($"your Kimi sign-in or model block holds a value coai will not write into an owned config.toml unescaped: {e.Message} (D19)");
        }
    }

    private static string ReadPersons(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>The installed Kimi's version, asked in the launch's own built environment on the owned home — or why it could not be.</summary>
    private static async Task<VersionAnswer> VersionAsync(KimiPlaces places, ProcessRequest request, IProcessLauncher launcher, CancellationToken ct)
    {
        try
        {
            // The launch request's own built PATH, so one PATH source feeds both the probe and the launch (and no environment is read here).
            var probe = KimiEnvironment.Built(new ProcessRequest(places.Executable, ["--version"], places.Home) { Timeout = VersionTimeout }, places, KimiEnvironment.PathOf(request));
            var asked = await launcher.RunAsync(probe, ct);
            var version = VersionWarnings.VersionIn(asked.StdOut);

            return asked.ExitCode == 0 && version.Length > 0
                ? new VersionAnswer(version, string.Empty)
                : new VersionAnswer(string.Empty, $"`{places.Executable} --version` answered no version (exit {asked.ExitCode}): {OneLine(asked.StdOut + " " + asked.StdErr)}");
        }
        catch (Win32Exception e)
        {
            return new VersionAnswer(string.Empty, $"'{places.Executable}' could not be started: {e.Message} — {VendorDiagnosis.InstallCure("kimi")}", CliMissing: true);
        }
    }

    /// <summary>The asked version, or why there is none — and whether the reason is that the OS could not start the CLI at all (a missing CLI, not a refusal).</summary>
    private sealed record VersionAnswer(string Version, string Refusal, bool CliMissing = false);

    /// <summary>Why the gate could not be taken: the key sentence when the folder cannot be named by its handle, else the lock's own.</summary>
    private static string LockRefusal(Hold.Unusable unusable, string target) =>
        KimiCredentialsLock.PathFor(target) is { Length: > 0 } path
            ? $"the Kimi sign-in lock {path} cannot be taken ({unusable.Cause.Message}) — fix the folder's permissions"
            : unusable.Cause.Message;

    /// <summary>The first non-empty sentence of the checks, run in order — later ones are not asked.</summary>
    private static string FirstOf(params Func<string>[] checks) =>
        checks.Select(check => check()).FirstOrDefault(said => said.Length > 0) ?? string.Empty;

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What a started launch holds: the sign-in's gate and the home's turn, given back in reverse, once, after the read.</summary>
    private sealed class Attempt(IDisposable turn, FileStream gate) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 1)
            {
                return;
            }

            try
            {
                gate.Dispose();
            }
            finally
            {
                turn.Dispose();
            }
        }
    }
}
