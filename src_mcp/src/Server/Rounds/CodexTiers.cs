using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What each codex executable of one round said about the standard tier — asked once per distinct executable, before the
/// round's roster is built (todo/PLAN_codex_tier_floor.md, change 3).
/// </summary>
/// <remarks>
/// <para><b>A value handed to the roster, not a lookup inside it.</b> <see cref="RosterBuilder.BuildWork"/> is synchronous
/// and stays so: the stage asks here, then passes the answers in, and the roster's <c>SettingsFor</c> copies one onto each
/// codex row's <see cref="ReviewerSettings.CodexTier"/> — the security lane's rows included, through the same settings.
/// A row this round did not ask about is <see cref="CodexTierSupport.Unprobed"/>, which sends what was always sent.</para>
/// <para><b>Only where the answer can change the argv:</b> a codex row on codex's OWN service whose fast mode is Off.
/// On sends <c>fast</c> to every release, "As the CLI is set" sends nothing, and a row on somebody else's endpoint is
/// told no tier at all (<c>CodexRuntime.TierArgs</c>) — asking those would spend half a second for an answer nothing
/// reads. Per EXECUTABLE rather than per row, because three codex rows on one CLI are one installed release.</para>
/// <para>Never cached across rounds: <see cref="CodexTierSupport"/> says why.</para>
/// </remarks>
internal sealed record CodexTiers(IReadOnlyDictionary<string, CodexTierSupport> ByExecutable)
{
    /// <summary>Nobody asked: every codex row stays <see cref="CodexTierSupport.Unprobed"/>.</summary>
    public static CodexTiers None { get; } = new(new Dictionary<string, CodexTierSupport>(StringComparer.Ordinal));

    /// <summary>What this row's executable said — or <see cref="CodexTierSupport.Unprobed"/> for a row nobody asked about.</summary>
    public CodexTierSupport For(ProviderSettings provider, IReviewerRuntime runtime) =>
        Asks(provider, runtime)
            ? ByExecutable.GetValueOrDefault(ExecutableOf(provider, runtime), CodexTierSupport.Unprobed)
            : CodexTierSupport.Unprobed;

    /// <summary>
    /// Asks every distinct executable among <paramref name="rows"/> whose answer can change an argv, at once, and logs
    /// each answer — an Unknown's reason is the one place a person learns why an Off row was still sent the tier.
    /// </summary>
    public static async Task<CodexTiers> AskAsync(
        IEnumerable<(ProviderSettings Provider, IReviewerRuntime Runtime)> rows,
        IProcessLauncher launcher,
        string workingDirectory,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        string[] executables = [.. rows.Where(row => Asks(row.Provider, row.Runtime)).Select(row => ExecutableOf(row.Provider, row.Runtime)).Distinct(StringComparer.Ordinal)];
        var answers = await Task.WhenAll(executables.Select(exe => CodexTierSupport.ProbeAsync(launcher, exe, workingDirectory, ct)));
        foreach (var (exe, answer) in executables.Zip(answers))
        {
            log.Information("codex tier probe: {Executable} — {Answer}: {Reason}", exe, answer.Answer, answer.Reason);
        }

        return new CodexTiers(executables.Zip(answers).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal));
    }

    /// <summary>A codex row on codex's own service, set Off — the only one whose argv the answer can change.</summary>
    private static bool Asks(ProviderSettings provider, IReviewerRuntime runtime) =>
        runtime is CodexRuntime codex && codex.TierDependsOnRelease(provider.Fast);

    /// <summary>The program the launch will start — the row's own path, else the runtime's default — so the probe asks THAT one.</summary>
    private static string ExecutableOf(ProviderSettings provider, IReviewerRuntime runtime) =>
        provider.ExecutablePath.Length > 0 ? provider.ExecutablePath : runtime.DefaultExecutable;
}
