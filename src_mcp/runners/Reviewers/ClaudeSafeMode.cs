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
/// <para><b>The way back.</b> <see cref="ReviewerSettings.SafeMode"/> off sends no flag and ALSO removes an inherited
/// <see cref="ClaudeCapability.SafeModeVariable"/> from the child's environment, or the switch would not be a way back for a person
/// whose shell already sets it. The env switch of E4 cannot remove an argv flag, which is why this is a setting of its own.</para>
/// </remarks>
/// <param name="Arguments">The argv words to add — <c>--safe-mode</c>, or none.</param>
/// <param name="Note">The sentence the person is owed about a launch that goes without the flag; empty when the flag is sent.</param>
/// <param name="ClearsTheVariable">Whether the child must NOT inherit <see cref="ClaudeCapability.SafeModeVariable"/> (the row switched safe mode off).</param>
public sealed record ClaudeSafeMode(IReadOnlyList<string> Arguments, string Note, bool ClearsTheVariable)
{
    /// <summary>What reaches a reviewer that runs without safe mode — said in every note, so they cannot drift.</summary>
    private const string Reaches = "the reviewed branch's CLAUDE.md and hooks reach this reviewer";

    /// <summary>The decision for one launch, from the row's switch and what the installed CLI's help said.</summary>
    public static ClaudeSafeMode For(ReviewerSettings settings) =>
        !settings.SafeMode
            ? new([], $"safe mode is switched off for this claude row, so {Reaches}", ClearsTheVariable: true)
            : settings.ClaudeCli.SafeMode == SafeModeSupport.Declared
                ? new([ClaudeCapability.SafeModeFlag], string.Empty, ClearsTheVariable: false)
                : new([], NoteWithout(settings.ClaudeCli), ClearsTheVariable: false);

    /// <summary>Why the flag was left out, for a row that wants it.</summary>
    private static string NoteWithout(ClaudeCapability cli) => cli.SafeMode switch
    {
        SafeModeSupport.NotDeclared => $"the installed claude does not declare {ClaudeCapability.SafeModeFlag}; {Reaches}",
        SafeModeSupport.Unknown => $"the installed claude's {ClaudeCapability.SafeModeFlag} capability could not be read ({cli.Reason}), so it was not sent; {Reaches}",
        _ => $"the installed claude was not asked about {ClaudeCapability.SafeModeFlag} for this launch, so it was not sent; {Reaches}",
    };
}
