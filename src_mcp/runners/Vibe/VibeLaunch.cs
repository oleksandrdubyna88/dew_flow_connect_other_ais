using System.ComponentModel;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// The models coai launches Vibe on (D10 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): a curated list, checked BEFORE the
/// launch, because Vibe does not refuse an alias it does not know — it exits 0 and answers on its routed default
/// (research/RESULTS_vibe_confinement.md §11, 3 of 3).
/// </summary>
public static class VibeModels
{
    /// <summary>Vibe's own default in an empty home (§11 arm a, §24) — what a row that names no model is launched on.</summary>
    public const string Default = "mistral-medium-3.5";

    /// <summary>
    /// The aliases this build has seen Vibe accept and report back as its <c>active_model</c>: <c>mistral-medium-3.5</c>
    /// (§11, §24) and <c>glm-5-3</c> (§17, 3 of 3). Labelled with the Vibe version they were seen on; a new alias joins
    /// when a run shows it, not when a list says it exists.
    /// </summary>
    public static IReadOnlyList<string> Curated { get; } = [Default, "glm-5-3"];

    /// <summary>The Vibe version the curated list was seen on.</summary>
    public const string SeenOn = "2.26.1";
}

/// <summary>
/// Everything a Vibe launch is checked against before it starts — the ONE launch control every Vibe launch path goes
/// through (D8b, D17, D18; <c>VibeRuntime.TakeTurnAsync</c>, which <c>ReviewerExecutor.LaunchAsync</c> asks before every
/// launch).
/// </summary>
/// <remarks>
/// In order: the host (D18), the built environment (D17), the model (D10), the home (created when absent, a link refused),
/// the home's TURN (taken before anything reads the home), the installed Vibe's version (asked in the same built
/// environment), the manifest entry for that version, and the home against it. Every refusal is a sentence naming what
/// was found and its cure; a refusal after the turn was taken gives the turn back.
/// </remarks>
public static class VibeLaunch
{
    /// <summary>The variables of the built environment (D17): exactly these, on top of <see cref="ProcessEnvironment.Minimal"/>.</summary>
    public const string HomeVariable = "VIBE_HOME";

    public const string CliVariable = "VIBE_CLI";

    public const string ModelVariable = "VIBE_ACTIVE_MODEL";

    /// <summary>
    /// Python's UTF-8 mode, on (research/RESULTS_vibe_confinement.md §26, 2026-10-13). Under the built environment CPython
    /// 3.14 on Windows writes the console code page (cp1252) to a pipe, and <c>--output json</c> echoes the whole
    /// conversation — so any character outside cp1252 in the prompt, the plan, the diff or the answer (<c>→</c>, <c>≤</c>,
    /// CJK, an emoji) killed the run with exit 2 AFTER the paid review. Measured: <c>PYTHONUTF8=1</c>, exit 0 and the answer
    /// <c>a → b</c>. <c>PYTHONIOENCODING</c> is NOT added: it is the narrower switch (the three standard streams only), it was
    /// not the one measured, and UTF-8 mode already sets those streams to UTF-8 — a second name would be an unmeasured one.
    /// </summary>
    public const string Utf8Variable = "PYTHONUTF8";

    /// <summary>The value <see cref="Utf8Variable"/> carries: on.</summary>
    public const string Utf8On = "1";

    /// <summary>
    /// The CLI coai measured. <c>vibe</c> is two CLIs, and a remote flag cached in the home switched this account to the
    /// Rust one between two launches — which refuses <c>--time-limit</c> and <c>--output-dir</c> and is unmeasured (§3).
    /// </summary>
    public const string PythonCli = "python";

    /// <summary>How long the <c>--version</c> asked before every launch may take.</summary>
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(60);

    private static readonly Regex VersionPattern = new(@"\b(\d+\.\d+\.\d+)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// D18: Vibe only where it was measured — a Windows host, starting a Windows <c>vibe</c>. Every number in the RESULTS
    /// is Windows; on Linux Vibe's keyring may fall back to a file inside <c>VIBE_HOME</c>, which would break D17, and
    /// nothing was measured there. A WSL bridge named as the executable is the Linux side by another door.
    /// </summary>
    public static string HostRefusal(HostKind host, string executable) =>
        host != HostKind.Windows
            ? $"Vibe is refused on a {HostKinds.Word(host)} host until it is measured there (D18 of todo/PLAN_vibe_and_kimi_runtimes.md) — every Vibe measurement coai has is Windows"
            : RunsInWsl(executable)
                ? $"'{executable}' would start Vibe on the Linux side, which is refused until it is measured there (D18 of todo/PLAN_vibe_and_kimi_runtimes.md) — point this row at a Windows vibe"
                : string.Empty;

    /// <remarks>The file name is cut at EITHER separator, not by <c>Path.GetFileName</c>: a Windows path is judged the same on a Linux test host.</remarks>
    private static bool RunsInWsl(string executable)
    {
        var trimmed = executable.Trim();
        var name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];

        return name.Equals("wsl", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wsl.exe", StringComparison.OrdinalIgnoreCase)
            || executable.TrimStart().StartsWith(@"\\wsl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// D17: the launch is the built one — not inheriting this process's environment, keeping only the minimal names, and
    /// carrying exactly the three Vibe variables with the Python CLI pinned, and Python's UTF-8 mode on (§26). A path that built a Vibe launch any other way
    /// is refused here rather than started (one launch control; §22 A: a <c>VIBE_*</c> the child inherits configures Vibe
    /// whatever its home holds).
    /// </summary>
    public static string EnvironmentRefusal(ProcessRequest request) =>
        Confined(request) && CarriesTheThree(request)
            ? string.Empty
            : $"this Vibe launch was not built with coai's own environment (the minimal set plus {HomeVariable}, {CliVariable}={PythonCli}, {ModelVariable} and {Utf8Variable}={Utf8On}, nothing inherited) and is refused (D17)";

    /// <summary>
    /// <paramref name="request"/> in the BUILT environment (D17) — the one builder every Vibe child goes through: a review
    /// launch (<c>VibeRuntime.Build</c>) and the <c>providers</c> probe alike, so the two cannot drift. What
    /// <see cref="EnvironmentRefusal"/> admits is exactly what this makes.
    /// </summary>
    public static ProcessRequest Built(ProcessRequest request, string home, string model) => request with
    {
        InheritsEnvironment = false,
        Passthrough = ProcessEnvironment.Minimal,
        Environment = new Dictionary<string, string?>
        {
            [HomeVariable] = home,
            [CliVariable] = PythonCli,
            [ModelVariable] = model,
            [Utf8Variable] = Utf8On,
        },
    };

    /// <summary>Nothing inherited but the platform's minimal names.</summary>
    private static bool Confined(ProcessRequest request) =>
        !request.InheritsEnvironment && request.Passthrough.SetEquals(ProcessEnvironment.Minimal);

    /// <summary>Exactly the built names — the three Vibe variables and UTF-8 mode — the Python CLI pinned, UTF-8 on and a home named.</summary>
    private static bool CarriesTheThree(ProcessRequest request) =>
        request.Environment.Count == 4
        && request.Environment.Keys.All(name => name is HomeVariable or CliVariable or ModelVariable or Utf8Variable)
        && Pinned(request)
        && Value(request, HomeVariable).Length > 0;

    /// <summary>The two fixed values: the measured CLI, and UTF-8 mode on (§3, §26).</summary>
    private static bool Pinned(ProcessRequest request) =>
        Value(request, CliVariable) == PythonCli && Value(request, Utf8Variable) == Utf8On;

    /// <summary>D10, before the launch: an alias outside the curated list is refused with the list.</summary>
    public static string ModelRefusal(string model) =>
        VibeModels.Curated.Contains(model, StringComparer.Ordinal)
            ? string.Empty
            : $"the model '{model}' is not one coai has seen Vibe accept — choose one of {string.Join(", ", VibeModels.Curated)} "
              + "(D10: Vibe answers an unknown alias on its own default model instead of refusing it)";

    /// <summary>The version a <c>vibe --version</c> printed (<c>python.exe …\vibe 2.26.1</c>), or empty.</summary>
    public static string VersionIn(string printed) =>
        VersionPattern.Match(printed) is { Success: true } found ? found.Groups[1].Value : string.Empty;

    /// <summary>The value a request carries for <paramref name="name"/>, or empty.</summary>
    public static string Value(ProcessRequest request, string name) =>
        request.Environment.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;

    /// <summary>
    /// The whole check, in order, ending holding the home's turn — or a refusal with nothing held. <paramref name="exportFolder"/>
    /// is the attempt's own, deleted when the turn is given back (D7: no export outlives its read).
    /// </summary>
    public static async Task<LaunchTurn> TakeAsync(HostKind host, ProcessRequest request, string exportFolder, IProcessLauncher launcher, CancellationToken ct)
    {
        var before = FirstOf(
            () => HostRefusal(host, request.Executable),
            () => EnvironmentRefusal(request),
            () => ModelRefusal(Value(request, ModelVariable)),
            () => HomeMade(Value(request, HomeVariable)));
        if (before.Length > 0)
        {
            return LaunchTurn.Refused(before);
        }

        var turn = await VibeHomeTurns.TakeAsync(Value(request, HomeVariable), ct);
        HomeVerdict inside;
        try
        {
            inside = await CheckedAsync(request, launcher, ct);
        }
        catch
        {
            turn.Dispose();
            throw;
        }

        return inside.Refusal.Length > 0 ? Given(turn, inside.Refusal) : LaunchTurn.Holding(new Attempt(turn, exportFolder), inside.Warning, inside.CliVersion);
    }

    /// <summary>What the check inside the turn concluded: a refusal, or a go — with a warning when the version was not measured.</summary>
    internal sealed record HomeVerdict(string Refusal, string Warning)
    {
        public static HomeVerdict Refuse(string refusal) => new(refusal, string.Empty);

        /// <summary>The release the check asked, as the ledger names it (<c>vibe 2.26.1</c>) — empty until it was asked.</summary>
        public string CliVersion { get; init; } = string.Empty;
    }

    /// <summary>Inside the turn: the manifest this build carries, then the home against it.</summary>
    private static async Task<HomeVerdict> CheckedAsync(ProcessRequest request, IProcessLauncher launcher, CancellationToken ct) =>
        VibeHomeManifest.Builtin switch
        {
            VibeManifestLoad.Loaded { Manifest: var manifest } => await AgainstAsync(manifest, request, launcher, ct),
            VibeManifestLoad.Broken broken => HomeVerdict.Refuse(broken.Reason),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    /// <summary>
    /// The home's SHAPE first — before anything of Vibe's starts on it, the version probe included, so a <c>hooks.toml</c> or
    /// a link is never in the home while Vibe runs — then the installed version, then the shape AGAIN on both branches (the
    /// <c>--version</c> process is Vibe's own and runs on the home, so what it leaves is checked before the review starts —
    /// risk consultation 6b4726a6, 2026-10-13), then the plugin bytes when that version was measured.
    /// </summary>
    private static async Task<HomeVerdict> AgainstAsync(VibeHomeManifest manifest, ProcessRequest request, IProcessLauncher launcher, CancellationToken ct)
    {
        var home = Value(request, HomeVariable);
        var shape = VibeHome.Refusal(home, manifest);
        if (shape.Length > 0)
        {
            return HomeVerdict.Refuse(shape);
        }

        var version = await VersionAsync(request, launcher, ct);
        var after = FirstOf(() => version.Refusal, () => VibeHome.Refusal(home, manifest));

        return after.Length > 0 ? HomeVerdict.Refuse(after) : Verdict(manifest, home, version.Version) with { CliVersion = Named(version.Version) };
    }

    private static LaunchTurn Given(IDisposable turn, string refusal)
    {
        turn.Dispose();

        return LaunchTurn.Refused(refusal);
    }

    /// <summary>The first non-empty sentence of the checks, run in order — later ones are not asked.</summary>
    private static string FirstOf(params Func<string>[] checks) =>
        checks.Select(check => check()).FirstOrDefault(said => said.Length > 0) ?? string.Empty;

    /// <summary>The home made when it is not there yet — an owned home starts empty.</summary>
    private static string HomeMade(string home)
    {
        try
        {
            Directory.CreateDirectory(home);

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"the coai-owned Vibe home {home} could not be made ({e.Message})";
        }
    }

    /// <summary>The installed Vibe's version, asked in the launch's own built environment — or why it could not be.</summary>
    private static async Task<(string Version, string Refusal)> VersionAsync(ProcessRequest request, IProcessLauncher launcher, CancellationToken ct)
    {
        try
        {
            var asked = await launcher.RunAsync(request with { Arguments = ["--version"], StdIn = string.Empty, Timeout = VersionTimeout }, ct);
            var version = VersionIn(asked.StdOut);

            return asked.ExitCode == 0 && version.Length > 0
                ? (version, string.Empty)
                : (string.Empty, $"`{request.Executable} --version` answered no version (exit {asked.ExitCode}): {OneLine(asked.StdOut + " " + asked.StdErr)}");
        }
        catch (Win32Exception e)
        {
            return (string.Empty, $"'{request.Executable}' could not be started: {e.Message} — {VendorDiagnosis.InstallCure("vibe")}");
        }
    }

    /// <summary>
    /// The home against the manifest entry for <paramref name="version"/>. A MEASURED version has its plugin bytes enforced —
    /// a mismatch is tampered bytes of a known version, and stays a refusal. A version with NO entry is run with a warning
    /// (the owner's decision of 2026-10-13: "Vibe must WARN, not refuse — set it up once and it must work if it can"): its
    /// home's SHAPE was already checked before its `--version` started, so its plugin files are admitted by name.
    /// </summary>
    internal static HomeVerdict Verdict(VibeHomeManifest manifest, string home, string version) => manifest.For(version) switch
    {
        [var entry] => HomeVerdict.Refuse(VibeHome.Refusal(home, manifest, entry)),
        _ => new HomeVerdict(string.Empty, Unmeasured(manifest, version)),
    };

    /// <summary>
    /// The warning for a Vibe version nobody measured — the same sentence on the launch (<see cref="LaunchTurn.Warning"/>)
    /// and on the row's card (<c>VibeRuntime.VersionWarning</c>) — or empty for a measured one.
    /// </summary>
    public static string VersionWarning(VibeHomeManifest manifest, string version) =>
        version.Length == 0 || manifest.For(version).Count > 0 ? string.Empty : Unmeasured(manifest, version);

    private static string Unmeasured(VibeHomeManifest manifest, string version) =>
        $"Vibe {version} is not a version coai has measured (it measured {manifest.Known}); it is run anyway, but its "
        + $"confinement (research/RESULTS_vibe_confinement.md) and its home's plugin hashes are {manifest.Known}'s, and this "
        + "version's plugin files are admitted by name only — re-run `node scripts/vibe-home-manifest.mjs --write` and "
        + "`node scripts/probe-vibe-confinement.mjs` with this Vibe to measure it";

    /// <summary>
    /// The release as a ledger line names it: <c>vibe 2.26.1</c>. Never Vibe's raw <c>--version</c> line, which starts with the
    /// interpreter's path under the person's profile (<c>python.exe C:\Users\…\vibe 2.26.1</c>, §26).
    /// </summary>
    internal static string Named(string version) => version.Length > 0 ? "vibe " + version : string.Empty;

    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What a started launch holds: the home's turn and its export folder, both given back after the read.</summary>
    private sealed class Attempt(IDisposable turn, string exportFolder) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(exportFolder))
                {
                    GitScratch.DeleteEvenIfReadOnly(exportFolder);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left in the launch's coai-answers-* directory, which the six-hour scratch sweep removes (§7 of the plan).
            }
            finally
            {
                turn.Dispose();
            }
        }
    }
}
