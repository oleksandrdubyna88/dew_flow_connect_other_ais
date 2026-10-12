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
    /// exclusions of the sanitised snapshot (D6) — so the boundary refuses what the snapshot leaves out.
    /// </summary>
    public static readonly string[] Exclusions = ["AGENTS.md", "agents.md", ".kimi-code", ".vibe", ".agents/skills", ".git"];

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
            "balance_spent",
            "the Kimi allowance is spent (D14) — waiting does not clear it; top up or wait for the plan's reset",
            AcpFailureKind.Spent),
    ];

    public string Name => ProfileName;

    /// <summary>The installer's own path when the binary is there, else bare <c>kimi</c> for the PATH — see <see cref="InstalledExecutableFor"/>.</summary>
    public string DefaultExecutable => InstalledExecutable;

    public IReadOnlyList<string> Arguments => ["acp"];

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
    /// </remarks>
    public static string InstalledExecutableFor(HostKind host, string home, string? pathVariable, Func<string, bool> exists) =>
        host switch
        {
            HostKind.Windows => IfThere(Path.Combine(home, ".kimi-code", "bin", "kimi.exe"), exists),
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
