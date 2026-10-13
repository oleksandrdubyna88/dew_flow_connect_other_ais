using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Findings;
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
/// Python CLI, the measured one) and <c>VIBE_ACTIVE_MODEL</c> (§17: it takes effect under the minimal environment).
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
public sealed class VibeRuntime(string id, HostKind host) : IReviewerRuntime
{
    /// <summary>The adapter for this machine's host.</summary>
    public VibeRuntime(string id = "vibe")
        : this(id, HostKinds.Current)
    {
    }

    /// <summary>How far under the launch's timeout <c>--time-limit</c> is set, so Vibe writes its export before the kill (D8).</summary>
    public static readonly TimeSpan LimitMargin = TimeSpan.FromSeconds(10);

    public string Provider => id;

    /// <summary>
    /// <c>vibe</c> where <c>uv tool install mistral-vibe</c> puts it (<c>%USERPROFILE%\.local\bin\vibe.exe</c>) when it is
    /// there, else bare <c>vibe</c> for the PATH — the <c>agy</c> rule: an MCP server started before the install has a PATH
    /// without the tool folder. Windows only: Vibe is refused anywhere else (D18).
    /// </summary>
    public string DefaultExecutable =>
        InstalledExecutableFor(host, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists);

    /// <summary>D6: Vibe reads only its working folder (§10, §16), and loads its configuration from it — so a copy.</summary>
    public bool NeedsASanitisedSnapshot => true;

    /// <summary><see cref="DefaultExecutable"/> for a given host, profile and file test — pure.</summary>
    public static string InstalledExecutableFor(HostKind host, string userProfile, Func<string, bool> exists)
    {
        var installed = Path.Combine(userProfile, ".local", "bin", "vibe.exe");

        return host == HostKind.Windows && userProfile.Length > 0 && exists(installed) ? installed : "vibe";
    }

    public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings)
    {
        var model = settings.Model.Length > 0 ? settings.Model : VibeModels.Default;
        var request = new ProcessRequest(
            settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : DefaultExecutable,
            Arguments(worktreePath, ExportFolder(outputDir, role, Guid.NewGuid()), settings.Timeout),
            worktreePath)
        {
            StdIn = prompt,
            Timeout = settings.Timeout,
            InheritsEnvironment = false,
            Passthrough = ProcessEnvironment.Minimal,
            Environment = new Dictionary<string, string?>
            {
                [VibeLaunch.HomeVariable] = HomeFor(settings.OwnedScratch.Length > 0 ? settings.OwnedScratch : outputDir, Provider, role),
                [VibeLaunch.CliVariable] = VibeLaunch.PythonCli,
                [VibeLaunch.ModelVariable] = model,
            },
        };

        // No effort and no tier: neither is measured taking effect on Vibe (shared/feature-availability.json), so none is sent.
        return new ReviewerInvocation(Provider, role, request, string.Empty, this, Model: model);
    }

    /// <summary>
    /// The shipped flag set (D2, D3, D7, D8) — the same argv <c>scripts/lib/vibe.mjs</c>'s <c>vibeArgs</c> measured. Never
    /// <c>--trust</c>, never <c>--add-dir</c>, never the prompt.
    /// </summary>
    internal static IReadOnlyList<string> Arguments(string workdir, string exportFolder, TimeSpan timeout) =>
    [
        "-p",
        "--output", "json",
        "--agent", "plan",
        "--enabled-tools", "read_file",
        "--workdir", workdir,
        "--time-limit", TimeLimit(timeout).ToString(System.Globalization.CultureInfo.InvariantCulture),
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
        Path.Combine(scratch, "vh-" + Short($"{provider}/{role}"));

    /// <summary>One attempt's export folder, in the launch's own directory — a fresh name every attempt.</summary>
    internal static string ExportFolder(string outputDir, string role, Guid attempt) =>
        Path.Combine(outputDir, "vx-" + Short($"{role}/{attempt:N}"));

    private static string Short(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..10];

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
        VibeLaunch.TakeAsync(host, invocation.Request, ExportFolderOf(invocation), launcher, ct);

    /// <summary>The last assistant message of <c>--output json</c>'s array (D7).</summary>
    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) => VibeStdout.Answer(result.StdOut);

    /// <summary>The export's usage; unknown, never zero, when it is missing or null (D8, D9).</summary>
    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result) => Export(invocation).Usage;

    /// <summary>Why the run failed, in the export's own words: its outcome, and its error when it carries one.</summary>
    public string WhyItFailed(ReviewerInvocation invocation, ProcessResult result) => Export(invocation) switch
    {
        { Exists: false } => string.Empty,
        { Outcome: "deadline" } => $"Vibe stopped at its own time limit (--time-limit, outcome: deadline) — {invocation.Request.Timeout.TotalSeconds:0} s was not enough for this review",
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
