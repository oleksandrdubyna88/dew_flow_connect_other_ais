namespace CoaiMcp.Core.QuestionConsult;

/// <summary>Where a planned launch stands: an empty scratch directory, or the first granted root.</summary>
/// <remarks>
/// Two kinds and not three. The plan's §4 also named <c>repo</c>, which is where the SHIPPED stuck
/// consultant stands (<c>LaunchConfinement.AsShipped</c>, decided by the adapter, not planned here);
/// no question row ever stands in the checkout, so a planner that could answer <c>repo</c> would be a
/// value nothing checks and one wrong arm away from a none row reading the tree.
/// </remarks>
public enum CwdKind
{
    Scratch,

    Root,
}

/// <summary>What the planner decided for one runtime × grant: the fragments to compose, or a refusal.</summary>
public abstract record Confinement
{
    /// <param name="Leading">Argv BEFORE the CLI's subcommand — codex's top-level <c>--search</c> (F1); empty everywhere else.</param>
    /// <param name="Flags">The sandbox flags, in order, complete — an adapter splices them and adds none of its own.</param>
    /// <param name="Cwd">Where the process stands.</param>
    /// <param name="AddDirs">The roots the flags grant, for the record; the flags already spell them the runtime's way.</param>
    /// <param name="Flag">D13's caveat on an admitted pair — shown beside every answer the row produces.</param>
    public sealed record Planned(
        string Runtime,
        CapabilityGrant Grant,
        IReadOnlyList<string> Leading,
        IReadOnlyList<string> Flags,
        CwdKind Cwd,
        IReadOnlyList<string> AddDirs,
        AdmissionFlag Flag) : Confinement;

    public sealed record Refused(string Reason) : Confinement;

    private Confinement() { }
}

/// <summary>
/// The ONE place a question row's sandbox is decided: runtime × grant → argv fragments, a cwd kind and
/// the granted roots (PLAN_question_consultant.md, D4). Adapters compose; they never choose a flag.
/// </summary>
/// <remarks>
/// <para>Every fragment is a measured fact of the probe run of 2026-10-01, cited per row in
/// <c>shared/runtime-capabilities.json</c>: claude launches ONLY through <c>--tools</c> (plus
/// <c>--restricted</c> for disk), never through <c>--disallowedTools</c> (F3, F5 — the shipped deny
/// lists were offered the user environment's whole tool set and leaked 6 of 6 through PowerShell);
/// codex's <c>--search</c> is a top-level flag before <c>exec</c> (F1); agy's measured launch is plan
/// mode on the stream (F7). The matrix is asked FIRST, so a pair it refuses is refused here with its
/// own sentence and no runtime arm is reached.</para>
/// <para>Pure: every flag is a golden test (<c>ConfinementPlannerTests</c>), per runtime × capability,
/// over the whole argv an adapter then builds.</para>
/// </remarks>
public static class ConfinementPlanner
{
    /// <summary>The flag no planned launch ever carries (F3/F5): a deny list is not a boundary.</summary>
    public const string NeverSent = "--disallowedTools";

    /// <summary>What a claude disk row may do: read, find, search — and no shell (F4).</summary>
    public static IReadOnlyList<string> ClaudeDiskTools { get; } = ["Read", "Glob", "Grep"];

    /// <summary>What a claude web row may do: the two client web tools, which refuse <c>file://</c> (F6).</summary>
    public static IReadOnlyList<string> ClaudeWebTools { get; } = ["WebSearch", "WebFetch"];

    /// <summary>
    /// Every argument that decides a sandbox on any of the five runtimes — what a planned launch may
    /// carry ONLY from a plan. The structural test in <c>ConfinementPlannerTests</c> reads it.
    /// </summary>
    private static readonly string[] SandboxVocabulary =
    [
        "--tools", "--restricted", "--add-dir", "--permission-mode", NeverSent, "--disallowed-tools", "--allowedTools",
        "--allowed-tools", "--dangerously-skip-permissions",
        "--search", "-s", "--sandbox", "--ephemeral", "-C", "--full-auto", "--yolo",
        "--mode",
    ];

    public static bool IsSandboxVocabulary(string argument) => SandboxVocabulary.Contains(argument, StringComparer.Ordinal);

    /// <summary>The plan for one row, or why there is none.</summary>
    public static Confinement Plan(string runtime, CapabilityGrant grant)
    {
        if (CapabilityMatrix.Admit(runtime, grant.Capability) is not Admission.Admitted admitted)
        {
            return new Confinement.Refused(((Admission.Refused)CapabilityMatrix.Admit(runtime, grant.Capability)).Reason);
        }

        var roots = WhyNotTheseRoots(grant);

        return roots.Length > 0
            ? new Confinement.Refused(roots)
            : Fragments(runtime.Trim().ToLowerInvariant(), grant, admitted.Flag);
    }

    /// <summary>A disk grant has roots, every root is absolute, and nothing else carries one.</summary>
    private static string WhyNotTheseRoots(CapabilityGrant grant)
    {
        if (grant.Capability != Capability.Disk)
        {
            return grant.Roots.Count == 0
                ? string.Empty
                : $"a '{grant.Capability.Spelled()}' grant carries no roots — a directory on a row that reads no directory is a leak path; the roots are a 'disk' prompt's";
        }

        if (grant.Roots.Count == 0)
        {
            return "a 'disk' grant needs at least one root to read — set the folders in ConnectOtherAIs > Question consultant";
        }

        return grant.Roots.FirstOrDefault(root => !IsAbsolute(root)) is { } relative
            ? $"the root '{relative}' is not an absolute path — a relative root resolves against whatever the cwd happens to be; every root is spelled from a drive or from /"
            : string.Empty;
    }

    /// <summary>
    /// Absolute on either platform's spelling — lexical, so the same table and the same tests decide
    /// alike on Windows and on Linux (a <c>D:/</c> root is not rooted to <c>Path.IsPathRooted</c> on Linux).
    /// </summary>
    private static bool IsAbsolute(string root)
    {
        var text = root.Trim();

        return text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '/' or '\\'
            || text.Length >= 2 && text[0] is '/' or '\\';
    }

    private static Confinement Fragments(string runtime, CapabilityGrant grant, AdmissionFlag flag) => runtime switch
    {
        "claude" => Claude(grant, flag),
        "codex" => Codex(grant, flag),
        "antigravity" => Antigravity(grant, flag),
        // A completion has no tools, so it has no sandbox flag: the question alone, from nowhere.
        _ => new Confinement.Planned(runtime, grant, [], [], CwdKind.Scratch, [], flag),
    };

    private static Confinement.Planned Claude(CapabilityGrant grant, AdmissionFlag flag) => grant.Capability switch
    {
        // `--tools ""` offers nothing at all (F3, read-denied × allowlist 3 of 3).
        Capability.None => new("claude", grant, [], ["--permission-mode", "plan", "--tools", ""], CwdKind.Scratch, [], flag),
        // `--restricted` confines the file tools to the working directories and `--add-dir` is the only
        // way out (F4); `--tools` alone is no path boundary.
        Capability.Disk => new("claude", grant, [],
            ["--permission-mode", "plan", "--restricted", "--tools", string.Join(',', ClaudeDiskTools), .. AddDirs("--add-dir", grant.Roots)],
            CwdKind.Root, grant.Roots, flag),
        // The two client web tools and nothing file-capable (F6: web-confined × allowlist 3 of 3).
        _ => new("claude", grant, [], ["--permission-mode", "plan", "--tools", string.Join(',', ClaudeWebTools)], CwdKind.Scratch, [], flag),
    };

    private static Confinement.Planned Codex(CapabilityGrant grant, AdmissionFlag flag) => grant.Capability switch
    {
        // `--ephemeral`: one-shot, nothing left in the person's codex store — the opposite of the
        // stuck consultant's trade, because a question row is never resumed.
        Capability.None => new("codex", grant, [], ["-s", "read-only", "--ephemeral"], CwdKind.Scratch, [], flag),
        // `-C <root 0>` tells it where to read; nothing stops it reading elsewhere (F2) — hence the flag.
        Capability.Disk => new("codex", grant, [], ["-s", "read-only", "--ephemeral", "-C", grant.Roots[0]], CwdKind.Root, grant.Roots, flag),
        // `--search` BEFORE `exec` (F1): `codex exec --search` is a usage error, exit 2.
        _ => new("codex", grant, ["--search"], ["-s", "read-only", "--ephemeral"], CwdKind.Scratch, [], flag),
    };

    private static Confinement.Planned Antigravity(CapabilityGrant grant, AdmissionFlag flag) => grant.Capability switch
    {
        Capability.Disk => new("antigravity", grant, [], ["--mode", "plan", .. AddDirs("--add-dir", grant.Roots)], CwdKind.Root, grant.Roots, flag),
        // `web` never reaches here: the matrix refuses it (F7, it cannot fetch a page headless).
        _ => new("antigravity", grant, [], ["--mode", "plan"], CwdKind.Scratch, [], flag),
    };

    private static IEnumerable<string> AddDirs(string flag, IReadOnlyList<string> roots) =>
        roots.SelectMany(root => (string[])[flag, root]);
}
