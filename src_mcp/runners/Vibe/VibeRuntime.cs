using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// Mistral Vibe (<c>vibe</c>, the Python CLI, on the person's own Mistral subscription) as a reviewer — S2 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>. Every flag and every check below cites the cell of
/// research/RESULTS_vibe_confinement.md it rests on; all of them are Windows, 3 of 3, Vibe 2.26.1.
/// </summary>
/// <remarks>
/// <para><b>Read-only, twice, by Vibe's own handling</b> (D2): <c>--agent plan --enabled-tools read_file</c>, both, always —
/// together they denied 9 of 9 write attempts (§4) and every write under forced experiment variants (§23). They share one
/// failure mode, so the second, independent block is not a flag: the working folder is a sanitised SNAPSHOT of the tree
/// (<see cref="NeedsASanitisedSnapshot"/>, D6), which holds none of the branch's <c>.vibe/</c>, skills or
/// <c>AGENTS.md</c>, so whatever Vibe does with a trust decision, there is nothing of the branch's to load (§2, §19).</para>
/// <para><b>Never <c>--trust</c> or <c>--add-dir</c></b> (D3): a trusted folder's <c>.vibe/hooks.toml</c> runs (§5's
/// control), and <c>--add-dir</c> trusts its folder. Without them the headless run works and reads its cwd (§10).</para>
/// <para><b>A home of coai's own and a built environment</b> (D17): <c>VIBE_HOME</c> is a coai-owned folder — one per
/// reviewer of a round, inside the round's own scratch folder (<see cref="ReviewerSettings.OwnedScratch"/>) — and the child
/// is given <see cref="ProcessEnvironment.Minimal"/> plus exactly <c>VIBE_HOME</c>, <c>VIBE_CLI=python</c> (§3: the
/// Python CLI, the measured one), <c>VIBE_ACTIVE_MODEL</c> (§17: it takes effect under the minimal environment) and
/// <c>PYTHONUTF8=1</c> (§26: without it a character outside the console's code page killed the run with exit 2) — and, on a NATIVE Linux host only,
/// <c>PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring</c> (DN10, E2 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>: no keyring on the host is reachable, the sign-in comes only through the linked <c>.env</c>; measured
/// closed on Vibe 2.26.1 with keyring 25.7.0, research/RESULTS_vibe_confinement.md §32). The host is this adapter's own (<see cref="Built"/> forwards its kind), so no caller spells it twice.
/// Nothing is inherited: an MCP server supplied only through a <c>VIBE_*</c> variable starts under an empty home (§22 A).
/// Before every launch <see cref="TakeTurnAsync"/> checks all of it (<see cref="VibeLaunch"/>).</para>
/// <para><b>The prompt on stdin, the answer from <c>--output json</c>, usage from <c>--output-dir</c></b> (D7): a 33 KB
/// prompt went whole through stdin 3 of 3 (§13). The export folder is per ATTEMPT (<see cref="ForLaunch"/>), inside the
/// launch's own <c>coai-answers-*</c> directory, and deleted when the launch has been read.</para>
/// <para><b>A limit is a failure</b> (D8): <c>--time-limit</c> is a few seconds under the launch's timeout; Vibe then exits 3
/// and still writes its export (§18, 3 of 3), with <c>outcome: deadline</c> and usage null in 2 of 3 — so the outcome is
/// read, never <c>stop_reason</c>, and a null usage is unknown, never zero. Cost is never carried (D9).</para>
/// <para><b>The model, checked twice</b> (D10): before the launch against <see cref="VibeModels.Curated"/>, after it against
/// the export's <c>active_model</c> (<see cref="Disowned"/>) — an alias Vibe does not know exits 0 on its routed default
/// (§11).</para>
/// </remarks>
public sealed class VibeRuntime(string id, VibeHost host, VibePathBudget? budget = null) : IReviewerRuntime
{

    /// <summary>
    /// Resolved ONCE, where the adapter is made: the PROCESS's platform budget (<see cref="VibePathBudget.ForThisProcess"/>), not
    /// the <paramref name="host"/> a test injects — so a test that builds a Windows adapter on ubuntu is not judged by a Windows
    /// limit. A test passes <see cref="VibePathBudget.None"/>, or a budget of its own.
    /// </summary>
    private readonly VibePathBudget _budget = budget ?? VibePathBudget.ForThisProcess;

    /// <summary>The adapter for this machine's host — or the host a scenario's flow gave it (<see cref="VibeHost.Using"/>).</summary>
    public VibeRuntime(string id = "vibe")
        : this(id, VibeHost.Current)
    {
    }

    /// <summary>
    /// <b>A test's way in — internal on purpose.</b> An adapter given a bare <see cref="HostKind"/> carries NO sign-in unless it is handed one
    /// (so a test never reads the person's real <c>~/.vibe/.env</c>) and, when <paramref name="wsl"/> is unset, THIS machine's WSL generation.
    /// A product caller on it would run Vibe signed out on a WSL2 host without a word, or judge a WSL host by the wrong kernel. The public ways
    /// in are the id alone (the machine's host, <see cref="VibeHost.Current"/>) and a <see cref="VibeHost"/>.
    /// </summary>
    internal VibeRuntime(string id, HostKind host, VibePathBudget? budget = null, VibeSignIn? signIn = null, WslGeneration? wsl = null)
        : this(id, new VibeHost(host, wsl ?? WslGenerations.ForHost(host), signIn ?? VibeSignIn.None), budget)
    {
    }

    /// <summary>How far under the launch's timeout <c>--time-limit</c> is set, so Vibe writes its export before the kill (D8).</summary>
    public static readonly TimeSpan LimitMargin = TimeSpan.FromSeconds(10);

    public string Provider => id;

    /// <summary>
    /// <c>vibe</c> where <c>uv tool install mistral-vibe</c> puts it when it is there, else bare <c>vibe</c> for the PATH — the
    /// <c>agy</c> rule: an MCP server started before the install has a PATH without the tool folder, and the VS Code server's often lacks
    /// <c>~/.local/bin</c>. See <see cref="InstalledExecutableFor(HostKind, string, Func{string, string?}, Func{string, bool})"/> for the order.
    /// </summary>
    public string DefaultExecutable =>
        InstalledExecutableFor(host.Kind, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable, ExecutableResolver.IsInstalledFile);

    /// <summary>D6: Vibe reads only its working folder (§10, §16), and loads its configuration from it — so a copy.</summary>
    public bool NeedsASanitisedSnapshot => true;

    /// <summary>D17: every launch writes a session into a coai-owned <c>VIBE_HOME</c>.</summary>
    public bool KeepsAHome => true;

    /// <summary><see cref="InstalledExecutableFor(HostKind, string, Func{string, string?}, Func{string, bool})"/> in an environment that sets nothing — the Windows rule needs none.</summary>
    public static string InstalledExecutableFor(HostKind host, string userProfile, Func<string, bool> exists) =>
        InstalledExecutableFor(host, userProfile, _ => null, exists);

    /// <summary>
    /// <see cref="DefaultExecutable"/> for a given host, profile, environment and file test — pure. The row's own CLI path is first and is
    /// not asked here (<see cref="Build"/>); this answers what the row did NOT set.
    /// </summary>
    /// <remarks>
    /// <para><b>Windows:</b> <c>%USERPROFILE%\.local\bin\vibe.exe</c> when it is there, else bare <c>vibe</c>.</para>
    /// <para><b>WSL (and a Linux host):</b> (DL5 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>) the folders <c>uv tool install</c> puts an
    /// executable in, in UV'S OWN order — <c></c>, <c></c>, <c>/../bin</c>, <c>~/.local/bin</c>
    /// (<c>uv tool dir --bin</c>, read from uv 0.13.0's own help) — each counting only when <c>vibe</c> is THERE and executable; then
    /// bare <c>vibe</c> (the <c>PATH</c> walk); then the install cure when starting it fails. <b>The installer's folder comes before
    /// <c>PATH</c></b>: the server's <c>PATH</c> often lacks <c>~/.local/bin</c>, and when both hold a <c>vibe</c> the one in uv's folder is the
    /// one the panel's install and update buttons maintain — a <c>PATH</c> hit may be an older or foreign install. A person who wants
    /// another <c>vibe</c> sets the row's CLI path, which wins.</para>
    /// <para>Paths are spelled with <c>/</c> or <c>\</c> by the host's own rule, never <c>Path.Combine</c> — a Windows path is judged the same
    /// on a Linux test host (the Kimi profile's test went red on Linux and macOS CI for exactly that, research/module_tests.md, 2026-10-12).</para>
    /// </remarks>
    public static string InstalledExecutableFor(HostKind host, string userProfile, Func<string, string?> environment, Func<string, bool> exists) => host switch
    {
        HostKind.Windows => WindowsInstalled(userProfile, exists),
        HostKind.Wsl or HostKind.Linux => UvToolFolders(userProfile, environment).Select(folder => $"{folder}/vibe").FirstOrDefault(exists) ?? "vibe",
        _ => "vibe",
    };

    private static string WindowsInstalled(string userProfile, Func<string, bool> exists)
    {
        var installed = $@"{userProfile.TrimEnd('\\', '/')}\.local\bin\vibe.exe";

        return userProfile.Length > 0 && exists(installed) ? installed : "vibe";
    }

    /// <summary>The ABSOLUTE folders uv installs executables into, most preferred first; one it was not told about is not in the list.</summary>
    private static IEnumerable<string> UvToolFolders(string userProfile, Func<string, string?> environment)
    {
        var xdgData = Absolute(environment("XDG_DATA_HOME"));
        string[] folders =
        [
            Absolute(environment("UV_TOOL_BIN_DIR")),
            Absolute(environment("XDG_BIN_HOME")),
            xdgData.Contains('/') ? $"{xdgData[..xdgData.LastIndexOf('/')]}/bin" : string.Empty,
            userProfile.Length > 0 ? $"{userProfile.TrimEnd('/')}/.local/bin" : string.Empty,
        ];

        return folders.Where(folder => folder.Length > 0);
    }

    /// <summary>The folder with no trailing separator, when the variable names an absolute one — else empty (a relative <c>UV_TOOL_BIN_DIR</c> is cwd-relative, which a server's cwd is not the person's).</summary>
    private static string Absolute(string? folder) =>
        folder is { Length: > 0 } && folder.StartsWith('/') ? folder.TrimEnd('/') : string.Empty;

    public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings)
    {
        var model = settings.Model.Length > 0 ? settings.Model : VibeModels.Default;
        var request = Built(
            new ProcessRequest(
                settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : DefaultExecutable,
                Arguments(worktreePath, ExportFolder(OwnedOr(settings, outputDir), role, Guid.NewGuid()), settings.Timeout),
                worktreePath)
            {
                StdIn = prompt,
                Timeout = settings.Timeout,
            },
            HomeFor(OwnedOr(settings, outputDir), Provider, role),
            model);

        // No effort and no tier: neither is measured taking effect on Vibe (shared/feature-availability.json), so none is sent.
        return new ReviewerInvocation(Provider, role, request, string.Empty, this, Model: model);
    }

    /// <summary>
    /// The folder the home AND the attempt's export live in: the owned folder (a round's one-shot folder, short and private, D7),
    /// else the launch's own. The export is the whole conversation, so it does not go to TEMP when a private folder is owned; it is
    /// a sibling of the home, outside what the home check walks.
    /// </summary>
    private static string OwnedOr(ReviewerSettings settings, string outputDir) => settings.OwnedScratch.Length > 0 ? settings.OwnedScratch : outputDir;

    /// <summary>
    /// The <c>providers</c> probe's <c>--version</c>, in the launch control's BUILT environment on a coai-owned home made
    /// for it and deleted after it — never the person's own <c>~/.vibe</c>, which a bare start writes <c>vibehistory</c>
    /// into (research/RESULTS_vibe_confinement.md §24; D17: the person's home gains nothing from coai).
    /// </summary>
    public async Task<ProcessResult> ProbeVersionAsync(IProcessLauncher launcher, string executable, TimeSpan timeout, CancellationToken ct)
    {
        var home = Directory.CreateTempSubdirectory("coai-answers-").FullName;
        try
        {
            return await launcher.RunAsync(
                Built(new ProcessRequest(executable, ["--version"], home) { Timeout = timeout }, home, VibeModels.Default), ct);
        }
        finally
        {
            DeleteQuietly(home);
        }
    }

    /// <summary>Best effort: a probe home left behind is a <c>coai-answers-*</c> folder in temp, which the product's six-hour scratch sweep removes.</summary>
    private static void DeleteQuietly(string home)
    {
        try
        {
            Files.GitScratch.DeleteEvenIfReadOnly(home);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the sweep; the probe's answer is what the caller asked for.
        }
    }

    /// <summary>
    /// <see cref="VibeLaunch.Built"/> for THIS adapter's host — the one forwarder every Vibe child of this runtime goes through (the review launch, the
    /// <c>providers</c> probe, and the consultant's launches through <see cref="VibeConsultant"/>), so no caller spells the host's kind twice and none can
    /// build a launch for another host than the one the adapter was given.
    /// </summary>
    internal ProcessRequest Built(ProcessRequest request, string home, string model) => VibeLaunch.Built(request, home, model, host.Kind);

    /// <summary>
    /// The shipped flag set (D2, D3, D7, D8) — the same argv <c>scripts/lib/vibe.mjs</c>'s <c>vibeArgs</c> measured. Never
    /// <c>--trust</c>, never <c>--add-dir</c>, never the prompt.
    /// </summary>
    internal static IReadOnlyList<string> Arguments(string workdir, string exportFolder, TimeSpan timeout) =>
        Arguments(workdir, exportFolder, timeout, ConfinementPlanner.VibeReadOnly);

    /// <summary>
    /// The same argv with the read-only pair a question row's PLAN gave (<see cref="ConfinementPlanner"/> — the one place a
    /// row's sandbox is decided); every other launch passes <see cref="ConfinementPlanner.VibeReadOnly"/>, the same words.
    /// </summary>
    /// <param name="maxTurns">The cap on assistant turns (<see cref="VibeLimits.ConsultantTurns"/> for a consultant launch); 0 — a review — passes none.</param>
    internal static IReadOnlyList<string> Arguments(string workdir, string exportFolder, TimeSpan timeout, IReadOnlyList<string> readOnly, int maxTurns = 0) =>
    [
        "-p",
        "--output", "json",
        .. readOnly,
        "--workdir", workdir,
        "--time-limit", TimeLimit(timeout).ToString(System.Globalization.CultureInfo.InvariantCulture),
        .. maxTurns > 0 ? (string[])["--max-turns", maxTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)] : [],
        "--output-dir", exportFolder,
    ];

    /// <summary>Whole seconds, <see cref="LimitMargin"/> under the launch's timeout, at least one.</summary>
    internal static int TimeLimit(TimeSpan timeout) =>
        (int)Math.Max(1, Math.Floor((timeout - LimitMargin).TotalSeconds));

    /// <summary>
    /// The reviewer's own home: a SHORT name (a deep home broke on a >260-character path, research/RESULTS_vibe_and_kimi_first_probe.md
    /// §4), from the vendor and the role, so a repair or a retry of the same reviewer starts on the home its first launch left.
    /// </summary>
    internal static string HomeFor(string scratch, string provider, string role) =>
        Path.Combine(scratch, Short($"{provider}/{role}", 6));

    /// <summary>
    /// A consultant's home inside its owned folder: <c>h</c>. One Vibe lives in a conversation's folder, a question row's or a
    /// check's, so the provider/role hash <see cref="HomeFor"/> makes would buy nothing there — and every character counts
    /// against <see cref="VibePathBudget"/>.
    /// </summary>
    internal static string ConsultantHome(string owned) => Path.Combine(owned, "h");

    /// <summary>One attempt's export folder, in the launch's own directory — a fresh name every attempt.</summary>
    internal static string ExportFolder(string outputDir, string role, Guid attempt) =>
        Path.Combine(outputDir, "vx-" + Short($"{role}/{attempt:N}", 10));

    internal static string Short(string text, int length = 10) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length];

    /// <summary>
    /// The launch as it starts: <c>--time-limit</c> from the timeout this launch actually has (a repair's and a retry's are
    /// shorter), and an export folder of its own — a retry never reads its predecessor's export.
    /// </summary>
    public ReviewerInvocation ForLaunch(ReviewerInvocation invocation)
    {
        var args = invocation.Request.Arguments.ToArray();
        var export = Array.IndexOf(args, "--output-dir");
        var limit = Array.IndexOf(args, "--time-limit");
        if (!HasValue(args, export) || !HasValue(args, limit))
        {
            return invocation;
        }

        args[export + 1] = ExportFolder(Path.GetDirectoryName(args[export + 1]) ?? string.Empty, invocation.Role, Guid.NewGuid());
        args[limit + 1] = TimeLimit(invocation.Request.Timeout).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return invocation with { Request = invocation.Request with { Arguments = args } };
    }

    private static bool HasValue(string[] args, int flag) => flag >= 0 && flag + 1 < args.Length;

    /// <summary>The one launch control (D8b, D10, D17, D18) — <see cref="VibeLaunch.TakeAsync"/>.</summary>
    public Task<LaunchTurn> TakeTurnAsync(ReviewerInvocation invocation, IProcessLauncher launcher, CancellationToken ct) =>
        VibeLaunch.TakeAsync(host, invocation.Request, ExportFolderOf(invocation), launcher, _budget, ct);

    /// <summary>The last assistant message of <c>--output json</c>'s array (D7).</summary>
    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) => VibeStdout.Answer(result.StdOut);

    /// <summary>
    /// The card's warning for the version <c>providers</c> read: the launch control's own sentence for a Vibe version with no
    /// measured home manifest — empty for a measured one, and for a manifest this build could not read (the launch refuses
    /// then, and says why).
    /// </summary>
    public string VersionWarning(string printedVersion) =>
        VibeHomeManifest.Builtin is VibeManifestLoad.Loaded { Manifest: var manifest }
            ? VibeLaunch.VersionWarning(manifest, VibeLaunch.VersionIn(printedVersion), host.Platform, host.Kind)
            : string.Empty;

    /// <summary>The export's usage; unknown, never zero, when it is missing or null (D8, D9).</summary>
    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result) => Export(invocation).Usage;

    /// <summary>How the sentence for coai's turn cap begins — also how a question row recognises it (<see cref="IsACapStop"/>).</summary>
    private const string TurnCapStop = "Vibe stopped at coai's cap of ";

    private const string TokenCapStop = "Vibe stopped at a token cap (--max-tokens, outcome: token_limit)";

    /// <summary>Whether <paramref name="reason"/> is one of <see cref="WhyItFailed"/>'s sentences for a cap — what a question row adds its guidance to.</summary>
    public static bool IsACapStop(string reason) =>
        reason.StartsWith(TurnCapStop, StringComparison.Ordinal) || reason.StartsWith(TokenCapStop, StringComparison.Ordinal);

    /// <summary>Why the run failed, in the export's own words: its outcome, and its error when it carries one.</summary>
    public string WhyItFailed(ReviewerInvocation invocation, ProcessResult result) => Export(invocation) switch
    {
        { Exists: false } => string.Empty,
        { Outcome: "deadline" } => $"Vibe stopped at its own time limit (--time-limit, outcome: deadline) — {invocation.Request.Timeout.TotalSeconds:0} s was not enough for this review",
        { Outcome: "turn_limit" } => $"{TurnCapStop}{VibeLimits.ConsultantTurns} assistant turns (--max-turns) before it answered",
        { Outcome: "token_limit" } => $"{TokenCapStop} before it answered",
        { Error.Length: > 0 } failed => $"{failed.Error} (outcome: {failed.Outcome})",
        var other => $"Vibe ended with outcome: {other.Outcome}",
    };

    /// <summary>
    /// D10, after the run: an answer is Vibe's only when its export says it ran on the alias the launch asked for. An
    /// alias Vibe does not know exits 0 on a routed default (§11) — absent, empty or different is a failure, and the
    /// usage read before this stays with it. A match proves Vibe accepted the alias, not which backend answered.
    /// </summary>
    public string Disowned(ReviewerInvocation invocation, ProcessResult result) =>
        Mismatch(Export(invocation), VibeLaunch.Value(invocation.Request, VibeLaunch.ModelVariable));

    internal static string Mismatch(VibeExport export, string asked) => export switch
    {
        { Exists: false } => "Vibe wrote no export, so neither the model that answered nor what the run consumed can be checked (D10)",
        { Outcome: not "finished" } => $"Vibe exited 0 but its export says outcome: {export.Outcome}",
        { ActiveModel: var ran } when ran != asked =>
            $"Vibe answered on '{(ran.Length > 0 ? ran : "its own default")}', not on the row's '{asked}' — the alias was not taken (D10)",
        _ => string.Empty,
    };

    /// <summary>The attempt's export — none for a launch that names no export folder, never a file read relative to the cwd.</summary>
    private static VibeExport Export(ReviewerInvocation invocation) =>
        ExportFolderOf(invocation) is { Length: > 0 } folder ? VibeExport.In(folder) : VibeExport.Missing;

    private static string ExportFolderOf(ReviewerInvocation invocation)
    {
        var args = invocation.Request.Arguments;
        var at = args.ToList().IndexOf("--output-dir");

        return at >= 0 && at + 1 < args.Count ? args[at + 1] : string.Empty;
    }
}
