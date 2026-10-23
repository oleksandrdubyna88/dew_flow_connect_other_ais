namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What the claude REVIEWER's launch does about the reviewed branch's own configuration: <c>--safe-mode</c> when the installed CLI
/// declares it and the row has not switched it off, otherwise nothing sent — and a note saying so, never a refusal
/// (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2; research/RESULTS_claude_reviewer_safe_mode.md).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The reviewer runs with the reviewed worktree as its working directory, so the branch's <c>CLAUDE.md</c>
/// and <c>.claude/</c> hooks apply to the review of that branch — a headless claude in a worktree is observed to run that worktree's
/// project <c>SessionStart</c> hook. <c>--safe-mode</c> starts the CLI with every customization off while auth, model selection, built-in
/// tools and permissions work normally (both recorded help texts). <b>Never <c>--bare</c></b>: it skips the keychain read, which is the
/// browser sign-in.</para>
/// <para><b>Three outcomes besides "sent", each a launch that still runs.</b> A help that came back without the flag
/// (<see cref="SafeModeSupport.NotDeclared"/>) and one that never came back (<see cref="SafeModeSupport.Unknown"/> — a timeout, a cut
/// or a failed help is not "does not declare") both go without the flag, each with its own sentence. A launch nobody asked
/// (<see cref="SafeModeSupport.Unprobed"/>) says so too: the roster and the Team server both ask before they build, and the scenario
/// tests hold them to it. An unsupported-flag failure is NEVER retried without the flag — the executor has no such second launch.</para>
/// <para><b>Sent is not the same as observed.</b> A help that DECLARES the flag says the CLI accepts it; that the reviewed branch's project hook then does
/// not run was observed on <see cref="ObservedVersions"/> only (research/RESULTS_claude_reviewer_safe_mode.md §5). On any other release — or one whose
/// <c>--version</c> could not be read — the flag is still sent and the note says its effect is not observed there. Never a refusal.</para>
/// <para><b>The way back.</b> <see cref="ReviewerSettings.SafeMode"/> off sends no flag and ALSO removes an inherited
/// <see cref="ClaudeCapability.SafeModeVariable"/> from the child's environment, or the switch would not be a way back for a person
/// whose shell already sets it. The env switch of E4 cannot remove an argv flag, which is why this is a setting of its own.</para>
/// </remarks>
/// <param name="Arguments">The argv words to add — <c>--safe-mode</c>, or none.</param>
/// <param name="Note">The sentence the person is owed about a launch that goes without the flag — or that sends it on a release where its effect was not observed; empty when the flag is sent on an observed release.</param>
/// <param name="ClearsTheVariable">Whether the child must NOT inherit <see cref="ClaudeCapability.SafeModeVariable"/> (the row switched safe mode off).</param>
public sealed record ClaudeSafeMode(IReadOnlyList<string> Arguments, string Note, bool ClearsTheVariable)
{
    /// <summary>What reaches a reviewer that runs without safe mode — said in every note, so they cannot drift.</summary>
    private const string Reaches = "the reviewed branch's CLAUDE.md and hooks reach this reviewer";

    /// <summary>
    /// The claude releases on which the flag's EFFECT — a reviewed branch's project hook does not run, its <c>CLAUDE.md</c> does not apply — was
    /// OBSERVED, not merely documented by a help text: the one place the list lives. A canary run recorded in
    /// research/RESULTS_claude_reviewer_safe_mode.md §5 (2026-10-23, claude 2.1.309): the branch's own <c>.claude/settings.json</c> SessionStart hook, and in a second run
    /// its PreToolUse hook, ran without the flag and did not run with it. Observed for SessionStart and PreToolUse project hooks on 2.1.309; other hook events
    /// (PostToolUse, Stop, UserPromptSubmit) were not tried. A release is added here only by a new run recorded in that file — a version bump is not evidence.
    /// </summary>
    public static IReadOnlyList<ClaudeRelease> ObservedVersions { get; } = [ClaudeRelease.Parse("2.1.309")];

    /// <summary>The decision for one launch, from the row's switch and what the installed CLI's help said.</summary>
    public static ClaudeSafeMode For(ReviewerSettings settings) =>
        !settings.SafeMode
            ? new([], $"safe mode is switched off for this claude row, so {Reaches}", ClearsTheVariable: true)
            : settings.ClaudeCli.SafeMode == SafeModeSupport.Declared
                ? new([ClaudeCapability.SafeModeFlag], NoteWithSent(settings.ClaudeCli.Version), ClearsTheVariable: false)
                : new([], NoteWithout(settings.ClaudeCli), ClearsTheVariable: false);

    /// <summary>
    /// What the person is owed about a launch that SENT the flag: nothing on a release where its effect was observed (<see cref="ObservedVersions"/>), else a
    /// sentence saying the flag went but its effect was not observed on this release — a help that declares a flag is not a measurement of what it does.
    /// </summary>
    private static string NoteWithSent(ClaudeRelease version) =>
        ObservedVersions.Contains(version)
            ? string.Empty
            : $"{ClaudeCapability.SafeModeFlag} is sent; its effect (the reviewed branch's hooks and CLAUDE.md do not apply) was observed on claude {string.Join(", ", ObservedVersions)} only, {NotObservedOn(version)}";

    /// <summary>The tail of that sentence: the release by name, or — when the version could not be read — that the effect is therefore not observed for it.</summary>
    private static string NotObservedOn(ClaudeRelease version) =>
        version.IsKnown
            ? $"not on {version}"
            : "and the installed release is not known, so the effect is not observed for it";

    /// <summary>Why the flag was left out, for a row that wants it.</summary>
    private static string NoteWithout(ClaudeCapability cli) => cli.SafeMode switch
    {
        SafeModeSupport.NotDeclared => $"the installed claude does not declare {ClaudeCapability.SafeModeFlag}; {Reaches}",
        SafeModeSupport.Unknown => $"the installed claude's {ClaudeCapability.SafeModeFlag} capability could not be read ({cli.Reason}), so it was not sent; {Reaches}",
        _ => $"the installed claude was not asked about {ClaudeCapability.SafeModeFlag} for this launch, so it was not sent; {Reaches}",
    };
}
