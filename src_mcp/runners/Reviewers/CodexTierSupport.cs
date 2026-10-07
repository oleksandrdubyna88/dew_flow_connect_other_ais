using System.Text.RegularExpressions;
using CoaiMcp.Core.Catalog;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>What the installed codex's own <c>--version</c> said about the standard tier — or that nobody asked.</summary>
public enum StandardTier
{
    /// <summary>Nobody asked: the launch is told what it has been told since fast mode shipped.</summary>
    Unprobed = 0,

    /// <summary>The release is outside the refusing range: <c>service_tier=default</c> is read, or ignored, never refused.</summary>
    Accepts,

    /// <summary>The release is in the range that refuses <c>service_tier=default</c> at config load.</summary>
    RefusesStandard,

    /// <summary>The version did not come back, or came back in a shape nobody has seen: a timeout, an exit, a CLI that would not start.</summary>
    Unknown,
}

/// <summary>
/// Whether the INSTALLED codex can be told the standard tier — and why we believe it.
/// </summary>
/// <param name="Reason">One sentence for the log and for <c>providers</c>.</param>
/// <remarks>
/// <para><b>Why the argv has to ask.</b> Measured 2026-10-07 (research/RESULTS_codex_service_tier_versions_2026-10-07.md):
/// codex 0.110.0 through 0.130.0 take only <c>fast</c> or <c>flex</c> for <c>service_tier</c>, and the
/// <c>-c service_tier=default</c> an Off row has carried since coai-mcp 0.44.0 fails the whole launch at config load,
/// before any request. Releases before 0.110.0 ignore the key and 0.131.0 and later accept it — so one fixed argv breaks
/// every codex review, consultation and question row on twenty-one releases, or gives up Off everywhere else. The range is
/// DATA (<c>fastMode[codex].refusesStandard</c> in <c>shared/feature-availability.json</c>), beside the measurement.</para>
/// <para><b>Asked on every launch path, never cached</b> (todo/PLAN_codex_tier_floor.md, the plan round's findings 0, 1, 2
/// and 5), for the reason <see cref="ClaudeCapability"/> gives: a long-lived server's remembered answer survives an in-place
/// upgrade of the CLI, and a symlink or version-manager shim switches releases under the same path. The cost is the same
/// order as claude's help: <c>codex --version</c> took 442–601 ms on the measuring machine, five runs.</para>
/// <para><b><see cref="StandardTier.Unknown"/> sends what is sent today</b> — the standard tier — and is not asked again
/// (the plan round's finding 4). Omitting the tier on a version nobody could read would let a person's own
/// <c>service_tier = "priority"</c> make every review fast at 2–2.5× the cost, unasked: the reason Off exists. Unknown
/// arises when <c>--version</c> cannot run — and then the launch cannot either — or prints a shape no release has
/// printed, which only a FUTURE release could, and those accept <c>default</c>. If the refusal is met anyway,
/// <see cref="VendorDiagnosis"/> names it with its cure.</para>
/// </remarks>
public sealed partial record CodexTierSupport(StandardTier Answer, string Reason)
{
    /// <summary>The default of <see cref="ReviewerSettings.CodexTier"/>: nothing changes until a site asks.</summary>
    public static CodexTierSupport Unprobed { get; } = new(StandardTier.Unprobed, "the installed codex was never asked");

    /// <summary>A version that did not come back, or did not parse, and why.</summary>
    public static CodexTierSupport Unknown(string reason) => new(StandardTier.Unknown, reason);

    /// <summary>
    /// How long the CLI has to print its version, per ask.
    /// </summary>
    /// <remarks>
    /// Ten seconds against a measured 0.44–0.60 s, as <see cref="ClaudeCapability.ProbeTimeout"/>: a CLI that cannot say
    /// its own version in that time is not going to answer a review either. The launcher kills the whole process tree at
    /// it (<c>ProcessLauncher.EndItAsync</c>), so a hung <c>--version</c> leaves nothing running behind the launch.
    /// </remarks>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The installed release refuses the standard tier, and an Off launch must be sent no tier at all.</summary>
    public bool RefusesStandard => Answer == StandardTier.RefusesStandard;

    /// <summary>
    /// Asks <paramref name="executable"/> for its <c>--version</c> through the shared launcher, under
    /// <see cref="ProbeTimeout"/>, and reads the answer against the file's refusing range. Asked ONCE: an unknown answer
    /// sends what was always sent, so a second ask would buy nothing a launch needs.
    /// </summary>
    /// <remarks>The caller's cancellation is not an answer: it is rethrown, so a withdrawn launch is never read as Unknown.</remarks>
    public static Task<CodexTierSupport> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) =>
        ProbeAsync(launcher, executable, workingDirectory, ProbeTimeout, ct);

    /// <summary>The probe under a timeout of the test's choosing — the hung-child arm cannot wait ten seconds per run.</summary>
    internal static async Task<CodexTierSupport> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var result = await launcher.RunAsync(
                new ProcessRequest(executable, ["--version"], workingDirectory) { Timeout = timeout },
                ct);
            ct.ThrowIfCancellationRequested();

            return Read(result, timeout);
        }
        // What a missing or unrunnable executable throws out of Process.Start — the same three VendorProbe catches.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return Unknown($"codex --version could not be started ({e.Message})");
        }
    }

    /// <summary>
    /// What a <c>--version</c> line says about the tier, by the file's range — the probe's reading, and the one
    /// <c>providers</c> applies to the version its health probe already asked for, so the two cannot disagree.
    /// </summary>
    public static CodexTierSupport OfVersion(string printed) =>
        OfVersion(printed, FeatureAvailability.Builtin.FastModeOf("codex").RefusesStandard);

    /// <summary><see cref="OfVersion(string)"/> against a range of the caller's — pure, so every edge is a unit test.</summary>
    internal static CodexTierSupport OfVersion(string printed, ReleaseRange refuses) => ReleasesIn(printed) switch
    {
        [var release] => Judged(release, refuses),
        [] => Unknown($"codex --version did not name a release ('{FirstLineOf(printed)}')"),
        // The code round: a wrapper that prints its own banner before the CLI answers would otherwise be read as the
        // banner. Two releases in one answer cannot be told apart, so neither is believed.
        var many => Unknown($"codex --version named more than one release ({string.Join(", ", many)}), so which one launches is not known"),
    };

    /// <summary>Every distinct release the answer names — one when it is a plain <c>--version</c> line.</summary>
    private static IReadOnlyList<Version> ReleasesIn(string printed) =>
        [.. CodexCliRelease().Matches(printed)
            .SelectMany(named => Version.TryParse(named.Groups["release"].Value, out var release) ? [release] : Array.Empty<Version>())
            .Distinct()];

    private static CodexTierSupport Judged(Version release, ReleaseRange refuses) =>
        refuses.Contains(release)
            ? new(StandardTier.RefusesStandard, $"codex-cli {release} is one of {refuses.From}–{refuses.Through}, which refuse service_tier=default")
            : new(StandardTier.Accepts, $"codex-cli {release} accepts service_tier=default");

    /// <summary>What one finished <c>--version</c> says: a release only when it came back, cleanly, naming one.</summary>
    private static CodexTierSupport Read(ProcessResult result, TimeSpan timeout) => result switch
    {
        { TimedOut: true } => Unknown($"codex --version did not answer within {timeout.TotalSeconds:0.#} s"),
        { ExitCode: not 0 } => Unknown($"codex --version exited {result.ExitCode}"),
        _ => OfVersion(result.StdOut),
    };

    private static string FirstLineOf(string printed) => printed.Trim().Split('\n')[0].Trim();

    /// <summary><c>codex-cli X.Y.Z</c>, as every release measured prints it; a pre-release suffix is read as its release.</summary>
    [GeneratedRegex(@"\bcodex-cli\s+(?<release>[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodexCliRelease();
}
