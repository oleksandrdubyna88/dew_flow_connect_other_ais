using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What each claude executable of one round said about <c>--safe-mode</c> — asked once per distinct executable, before the round's roster is
/// built, every round (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2).
/// </summary>
/// <remarks>
/// <para><b>CodexTiers' twin, for the same reasons.</b> <see cref="RosterBuilder.BuildWork"/> is synchronous and stays so: the stage asks
/// here, then passes the answers in, and the roster's <c>SettingsFor</c> copies one onto each claude row's
/// <see cref="ReviewerSettings.ClaudeCli"/> — the security lane's rows included, through the same settings. A row this round did not
/// ask about is <see cref="ClaudeCapability.Unprobed"/>, which goes without the flag and says so.</para>
/// <para><b>Only where the answer can change the argv:</b> a claude row whose safe mode is ON. An Off row sends nothing whatever the
/// help says, and asking would spend a spawn for an answer nothing reads. Per EXECUTABLE rather than per row, because three claude rows
/// on one CLI are one installed release.</para>
/// <para><b>Never cached, not even across two rounds</b> (risk consultation 958e430d): a cache still needs a <c>--version</c> spawn to see
/// an in-place update, so it saves nothing. <see cref="ClaudeCapability.ProbeAsync(IProcessLauncher, string, string, CancellationToken)"/>
/// asks twice at most under its ten-second ceiling; a CLI that cannot print its help is Unknown, and the launch goes without the flag.</para>
/// </remarks>
internal sealed record ClaudeCapabilities(IReadOnlyDictionary<string, ClaudeCapability> ByExecutable)
{
    /// <summary>Nobody asked: every claude row stays <see cref="ClaudeCapability.Unprobed"/>.</summary>
    public static ClaudeCapabilities None { get; } = new(new Dictionary<string, ClaudeCapability>(StringComparer.Ordinal));

    /// <summary>What this row's executable said — or <see cref="ClaudeCapability.Unprobed"/> for a row nobody asked about.</summary>
    public ClaudeCapability For(ProviderSettings provider, IReviewerRuntime runtime) =>
        Asks(provider, runtime)
            ? ByExecutable.GetValueOrDefault(ExecutableOf(provider, runtime), ClaudeCapability.Unprobed)
            : ClaudeCapability.Unprobed;

    /// <summary>
    /// Asks every distinct executable among <paramref name="rows"/> whose answer can change an argv, at once, and logs each answer — an
    /// Unknown's reason is the one place a person learns why a reviewer went without the flag.
    /// </summary>
    public static async Task<ClaudeCapabilities> AskAsync(
        IEnumerable<(ProviderSettings Provider, IReviewerRuntime Runtime)> rows,
        IProcessLauncher launcher,
        string workingDirectory,
        Serilog.ILogger log,
        CancellationToken ct)
    {
        string[] executables = [.. rows.Where(row => Asks(row.Provider, row.Runtime)).Select(row => ExecutableOf(row.Provider, row.Runtime)).Distinct(StringComparer.Ordinal)];
        var answers = await Task.WhenAll(executables.Select(exe => AskOneAsync(launcher, exe, workingDirectory, ct)));
        foreach (var (exe, answer) in executables.Zip(answers))
        {
            log.Information("claude safe-mode probe: {Executable} — {Answer}, release {Version}: {Reason}", exe, answer.SafeMode, answer.Version.Length > 0 ? answer.Version : "not read", answer.Reason);
        }

        return new ClaudeCapabilities(executables.Zip(answers).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal));
    }

    /// <summary>
    /// One executable: its help, then — only when the help declares <c>--safe-mode</c> — its <c>--version</c>, so the reviewer's note can say whether the
    /// flag's effect was observed on THIS release (<see cref="ClaudeSafeMode.ObservedVersions"/>). Two spawns at most, once per round, never per launch.
    /// </summary>
    private static async Task<ClaudeCapability> AskOneAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) =>
        await (await ClaudeCapability.ProbeAsync(launcher, executable, workingDirectory, ct)).WithVersionAsync(launcher, executable, workingDirectory, ct);

    /// <summary>A claude row whose safe mode is on — the only one whose argv the answer can change.</summary>
    private static bool Asks(ProviderSettings provider, IReviewerRuntime runtime) =>
        runtime is ClaudeRuntime && provider.SafeMode;

    /// <summary>The program the launch will start — the row's own path, else the runtime's default — so the probe asks THAT one.</summary>
    private static string ExecutableOf(ProviderSettings provider, IReviewerRuntime runtime) =>
        provider.ExecutablePath.Length > 0 ? provider.ExecutablePath : runtime.DefaultExecutable;
}
