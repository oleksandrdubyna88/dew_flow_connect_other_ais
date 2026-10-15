using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// Kimi Code over <c>kimi acp</c> — the one ACP profile (D15 of <c>PLAN_vibe_and_kimi_runtimes.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Why ACP and not prompt mode.</b> <c>kimi -p</c> takes the prompt only as an argument (<c>-p -</c> is
/// a literal dash), a review prompt is over Windows' command line, and prompt mode runs with no approval at all
/// (<c>research/RESULTS_kimi_confinement.md</c> §1). Over ACP the prompt rides on stdin and coai is the CLIENT:
/// every write-kind tool asks <c>session/request_permission</c> first and the client's refusal leaves nothing
/// written (§2.1, 3 of 3), and with <c>fs.readTextFile</c> offered every <c>Read</c> — the asked files AND Kimi's
/// own search for its instruction files — comes through <c>fs/read_text_file</c>, where the boundary decides (§2.2).</para>
/// <para><b>The model is set every session.</b> A session comes up on Kimi's own default, which on this plan was
/// a model the plan does not include: the prompt failed <c>401 Your current subscription does not have access to
/// kimi-for-coding-highspeed</c>. <c>session/set_config_option {configId: "model"}</c> before the prompt fixed it
/// (§2) — that sentence is the first known failure below, listed before the sign-in one it begins with.</para>
/// <para><b>What this profile does NOT bound.</b> <c>Grep</c> and <c>Glob</c> read the disk directly — auto-allowed,
/// no permission request, no client call (§2.3) — so Kimi's READS are unbounded, as codex's are; the boundary
/// closes <c>Read</c> and the instruction-file search, and the plan says so rather than claiming confinement.</para>
/// </remarks>
public sealed class KimiAcpProfile : IAcpProfile
{
    /// <summary>The name a launch asks for this profile by.</summary>
    public const string ProfileName = "kimi";

    /// <summary>
    /// The names refused at every depth: both spellings Kimi searches for its instructions (<c>AGENTS.md</c>,
    /// <c>agents.md</c>, and <c>.kimi-code/AGENTS.md</c> through its folder), Vibe's folder and the shared
    /// exclusions of the sanitised snapshot (D6) — so the boundary refuses what the snapshot leaves out. M-K9 (e) added two
    /// (RESULTS section 21): <c>.agents/agents</c> (a sub-agent root Kimi loads from disk, a PATH suffix like <c>.agents/skills</c>) and
    /// <c>.mcp.json</c> (the file Kimi starts MCP servers from when the folder is trusted). The latter is a NAME, so it is refused at every
    /// depth — the parent plan says "project-root", and the stricter reading is what a name entry does.
    /// </summary>
    public static readonly string[] Exclusions = ["AGENTS.md", "agents.md", ".kimi-code", ".vibe", ".agents/skills", ".agents/agents", ".mcp.json", ".git"];

    /// <summary>The name of the spent-allowance failure — the field of the shim's report that <c>KimiRuntime</c> classifies a rate limit from, never the text.</summary>
    public const string BalanceSpent = "balance_spent";

    /// <summary>The sentences measured on 2026-10-12 (RESULTS §2), the specific one first.</summary>
    private static readonly AcpKnownFailure[] Failures =
    [
        new(
            "Your current subscription does not have access to",
            "subscription_model",
            "the Kimi Code plan does not include this model — pick one the plan has (kimi-code/k3-256k answered on this one) or upgrade the plan",
            AcpFailureKind.Refused),
        new(
            "Authentication required",
            "not_signed_in",
            "run `kimi login` in a terminal and try again",
            AcpFailureKind.Refused),
        new(
            "insufficient balance",
            BalanceSpent,
            "the Kimi allowance is spent (D14) — waiting does not clear it; top up or wait for the plan's reset",
            AcpFailureKind.Spent),
    ];

    /// <summary>
    /// The Kimi versions coai has measured. Every number in <c>research/RESULTS_kimi_confinement.md</c> — the write refusals, the
    /// owned home's inventory (section 18), the OS-home redirect (section 19), the model pin (section 20) — is 2.1.1's; a version
    /// joins this list when the whole probe (<c>scripts/probe-kimi-confinement.mjs</c>, whose shim arms speak the shim's <c>--home</c>/<c>--persons-bin</c>/<c>--locks</c> flags on the owned-root layout since E4.5 — proved by <c>scripts/lib/kimi-shim.test.mjs</c> without a paid run, the cells themselves not re-run on it yet) and <c>scripts/probe-kimi-tree-hold.mjs</c> have run on it. Any other version is run
    /// with a warning (the owner's decision of 2026-10-13), never refused.
    /// </summary>
    public static IReadOnlyList<string> MeasuredVersions { get; } = ["2.1.1"];

    public string Name => ProfileName;

    /// <summary>The installer's own path when the binary is there, else bare <c>kimi</c> for the PATH — see <see cref="InstalledExecutableFor"/>.</summary>
    public string DefaultExecutable => InstalledExecutable;

    public IReadOnlyList<string> Arguments => ["acp"];

    /// <summary>Kimi runs only on an owned home and only under the tree hold (D19, E4.2 decision 10): <c>--home</c>, <c>--persons-bin</c> and <c>--locks</c>.</summary>
    public bool NeedsAHome => true;

    /// <summary>
    /// <c>kimi acp</c> in the BUILT environment (D19) — the minimal names and the seven the launch control builds, never the shim's own. The
    /// person's paths are the two the runtime passed on the argv (<see cref="KimiPlaces.ForShim"/>); nothing is read from this process's environment
    /// but the <c>PATH</c> to keep.
    /// </summary>
    public ProcessRequest AgentRequest(AcpAsk ask) => KimiEnvironment.Built(new ProcessRequest(ask.Executable, Arguments, ask.Cwd), PlacesOf(ask));

    /// <summary>The launch control's own check (<see cref="KimiEnvironment.Refusal"/>) on the request about to start, against the places the argv named.</summary>
    public string AgentRefusal(ProcessRequest request, AcpAsk ask) => KimiEnvironment.Refusal(request, PlacesOf(ask));

    /// <summary>The sign-in's tree hold and the home's (<see cref="KimiShimHold"/>), taken immediately before the agent starts.</summary>
    public AcpHolding Hold(AcpAsk ask, TimeSpan within) => KimiShimHold.Taken(PlacesOf(ask), within);

    private static KimiPlaces PlacesOf(AcpAsk ask) => KimiPlaces.ForShim(ask.Home, ask.PersonsBin);

    /// <summary>The <c>session/new</c> config option that carries the model id (<c>kimi-code/k3-256k</c>, …).</summary>
    public string ModelOptionId => "model";

    public IReadOnlyList<string> ExcludedNames => Exclusions;

    public IReadOnlyList<AcpKnownFailure> KnownFailures => Failures;

    /// <summary>Where Moonshot's installer puts it, for a server whose shell started before the install ran.</summary>
    public static string InstalledExecutable =>
        InstalledExecutableFor(
            HostKinds.Current,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("PATH"),
            File.Exists);

    /// <summary>
    /// <see cref="InstalledExecutable"/> for a given host, home and file test.
    /// </summary>
    /// <remarks>
    /// <para>Windows: <c>%USERPROFILE%\.kimi-code\bin\kimi.exe</c> — where the 2.1.1 the owner signed in on lives
    /// (RESULTS header) — when it is there, else bare <c>kimi</c>, which <see cref="ExecutableResolver"/> walks the
    /// PATH for at launch. The same rule <c>agy</c> follows on Windows: an MCP server is usually started by an editor
    /// whose PATH predates the install.</para>
    /// <para>Every other host gets bare <c>kimi</c> and no guess: nothing here has run Kimi on Linux, WSL or a Mac,
    /// and a path nobody observed is not one to start a process from (the <c>agy</c> precedent for macOS). The WSL
    /// cells of S4 lift this when they run.</para>
    /// <para>Pure over <paramref name="host"/>, which means the Windows path is spelled with Windows separators
    /// whatever OS runs this: <c>Path.Combine</c> spelled it with the RUNNING OS's separator, so on Linux and macOS CI
    /// (2026-10-12) the path never equalled the one the file test was given and the answer was bare <c>kimi</c>.</para>
    /// </remarks>
    public static string InstalledExecutableFor(HostKind host, string home, string? pathVariable, Func<string, bool> exists) =>
        host switch
        {
            HostKind.Windows => IfThere($@"{home.TrimEnd('\\', '/')}\.kimi-code\bin\kimi.exe", exists),
            _ => ProfileName,
        };

    private static string IfThere(string installed, Func<string, bool> exists) => exists(installed) ? installed : ProfileName;
}

/// <summary>Every ACP profile this build has, by name.</summary>
/// <remarks>
/// A list rather than a switch on a string in the shim: the shim asks the catalog, and a vendor that is not here
/// is refused with the names that are — the same shape as <c>ApiVendors.Resolve</c>.
/// </remarks>
public static class AcpProfiles
{
    /// <summary>The profiles, in the order a refusal lists them.</summary>
    public static readonly IReadOnlyList<IAcpProfile> All = [new KimiAcpProfile()];

    /// <summary>The names, for a refusal that says what this build knows.</summary>
    public static IEnumerable<string> Names(IReadOnlyList<IAcpProfile> profiles) => profiles.Select(profile => profile.Name);

    /// <summary>The profile called <paramref name="name"/> (case-insensitive), or null when this build has none — the one legitimate "not found".</summary>
    public static IAcpProfile? Find(IReadOnlyList<IAcpProfile> profiles, string name) =>
        profiles.FirstOrDefault(profile => string.Equals(profile.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
