using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// How deep Vibe writes below its home, against the longest path the platform lets a process write (D2 of
/// <c>research/PLAN_vibe_live_round_fixes.md</c>) — the one adapter for these numbers
/// (<c>.agents/conventions/common/platform-limits.md</c> §1).
/// </summary>
/// <remarks>
/// <para><b>Windows without long paths stops a FILE path at 259 characters</b> (a folder at 247), counted in UTF-16 units — probed
/// 2026-10-15 with Vibe's own interpreter (research/RESULTS_vibe_confinement.md §27). The deepest path Vibe 2.26.1 writes below
/// its home is a session chunk's temporary file, <c>logs\session\unified\&lt;36&gt;\chunks\.&lt;64 hex&gt;.json.&lt;16 hex&gt;.tmp</c>,
/// 156 characters: the persisted chunk (134) wrapped by Vibe's atomic write (22). So a home over <c>259 − 1 − 156 = 102</c>
/// characters dies with <c>[Errno 2]</c> after the launch was paid for.</para>
/// <para><b>No long-paths exemption:</b> the registry flag says what Windows allows a long-path-AWARE process, not that Vibe's
/// interpreter is one, and nobody measured that branch (M-V18 of the plan does). <b>No margin:</b> the number is measured; a
/// later Vibe that writes deeper is caught when it is requalified (the manifest's <c>deepestChild</c>, held against
/// <see cref="Windows"/> by a test).</para>
/// <para>The budget is the PROCESS's platform (<see cref="OperatingSystem.IsWindows"/>), never the <c>HostKind</c> a test injects,
/// and there is a value for exactly one platform: Vibe is refused off Windows by D18's host check, which runs first, so no budget
/// elsewhere is ever the deciding check — and a number for a platform nobody probed is not written down.</para>
/// </remarks>
/// <param name="FileLimit">The longest file path the platform writes; 0 when there is no budget.</param>
/// <param name="DeepestChild">The characters Vibe writes below its home, wrapper included.</param>
public sealed record VibePathBudget(int FileLimit, int DeepestChild)
{
    /// <summary>
    /// Windows with <c>LongPathsEnabled = 0</c>, Vibe 2.26.1: probed 259 (<see cref="WindowsPathLimits.FileUnits"/>, the one
    /// constant) and 156 (§27).
    /// </summary>
    public static VibePathBudget Windows { get; } = new(WindowsPathLimits.FileUnits, 156);

    /// <summary>No budget: nothing is refused. A test that is about something else, and every platform but Windows.</summary>
    public static VibePathBudget None { get; } = new(0, 0);

    /// <summary>This process's budget: Windows' on Windows, none elsewhere (D18 refuses Vibe there first).</summary>
    public static VibePathBudget ForThisProcess => Overridden.Value ?? (OperatingSystem.IsWindows() ? Windows : None);

    private static readonly AsyncLocal<VibePathBudget?> Overridden = new();

    /// <summary>
    /// A test's seam, per logical flow: a scenario that drives the real service cannot hand its <see cref="VibeRuntime"/> a budget
    /// (the service builds it), and a long TEMP on a CI runner is not what such a scenario is about. Dispose restores the process's.
    /// </summary>
    internal static IDisposable Using(VibePathBudget budget)
    {
        var before = Overridden.Value;
        Overridden.Value = budget;

        return new Restore(() => Overridden.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>The longest home that fits (102 on Windows) — a pure number.</summary>
    private int LongestHome => FileLimit - 1 - DeepestChild;

    /// <summary>Empty when Vibe's deepest write below <paramref name="home"/> fits; else the sentence that says why not and what to do.</summary>
    public string Refusal(string home) => RefusalForLength(home, Path.GetFullPath(home).Length);

    /// <summary><see cref="Refusal"/> for a home of a known length in UTF-16 units — pure, so a layout can be checked as a string.</summary>
    internal string RefusalForLength(string home, int length) =>
        FileLimit == 0 || length <= LongestHome
            ? string.Empty
            : $"the coai-owned Vibe home {home} is {length} characters long, and Vibe writes {DeepestChild} characters below it "
              + $"(its session chunks, research/RESULTS_vibe_confinement.md §27) — {length + 1 + DeepestChild} is past Windows' "
              + $"{FileLimit}-character path limit, so coai does not start Vibe on it; set \"Vibe homes folder\" in ConnectOtherAIs "
              + "(COAI_CONSULTANT_HOMES) to a shorter local folder of your own — coai keeps its Vibe homes under it, private to you";
}
