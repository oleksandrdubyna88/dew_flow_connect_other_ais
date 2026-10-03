namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// What an antigravity consultant is told about its tools — once in the prompt, and once more, in the
/// same conversation, when a permission it reached for was auto-denied.
/// </summary>
/// <remarks>
/// <para><b>Every word here was measured before it was written.</b> On agy 1.2.15 in <c>--mode plan</c>,
/// 2026-10-02 (research/RESULTS_agy_consult_follow_up.md): the only read tool the model has is
/// <c>view_file</c> — <c>grep_search</c>, <c>find_by_name</c> and <c>list_dir</c> are declared by
/// <c>init</c> and the model said it does not have them; one denied <c>run_command</c> ends the turn
/// empty; and the follow-up text below, sent into the same conversation, answered in prose 6 times of 6,
/// on Windows and in WSL, without reaching for the shell again.</para>
/// <para><b>"Do not plan, schedule or delegate"</b> is in the text because the Windows runs did: a model
/// without its shell wrote a <c>plan.md</c> into its own brain directory and handed the work to a
/// <c>schedule</c> subagent, which a caller blocked on an answer cannot use.</para>
/// <para><b>The marker.</b> Every follow-up says <see cref="StaysDenied"/>; the prompt's
/// <see cref="Toolbox"/> must not. The two are told apart by that phrase — by the scenario tests' fake
/// CLI today — so a toolbox sentence that grew the phrase would make every first turn look like a
/// follow-up. A test asserts it is absent.</para>
/// </remarks>
public static class AntigravityFollowUps
{
    /// <summary>The phrase every follow-up carries and the first prompt never does.</summary>
    public const string StaysDenied = "will stay denied";

    /// <summary>The permission word agy reports for a denied <c>run_command</c>.</summary>
    public const string CommandAction = "command";

    /// <summary>The permission words agy reports for a denied read outside the checkout.</summary>
    public static IReadOnlyList<string> ReadActions { get; } = ["read_file", "read_url"];

    /// <summary>The prompt's sentence under "## What you have": the one read tool observed working, and the shell's standing.</summary>
    public const string Toolbox =
        "Your only tool for reading this checkout is view_file: open the files named in this prompt, and others "
        + "by their path. Shell commands (run_command) are refused automatically in this mode — if a check needs "
        + "one, name it in your answer and the caller will run it.";

    /// <summary>The follow-up for a denied shell command — the text measured 6 of 6, word for word.</summary>
    public const string NoCommands =
        "Shell commands are not available in this consultation: run_command was denied and " + StaysDenied + ". "
        + "Do not call it again, and do not plan, schedule or delegate the work. Read the files you need with "
        + "view_file, then answer now, in plain prose, from what you have read. If a check needs a command, name "
        + "the exact command for the caller to run.";

    /// <summary>
    /// The follow-up for <paramref name="denied"/>: the shell's text when a command was among them, the read
    /// text naming the permission when a read was — and EMPTY for anything else, which gets no follow-up.
    /// </summary>
    /// <remarks>
    /// <para><b>An allowlist, not a fallback.</b> Only three words were ever seen denied in a consultation
    /// (<c>command</c>, <c>read_file</c>, <c>read_url</c>), and only the first has a follow-up that was
    /// measured to work. A denied <c>write_file</c> is a consultant that tried to EDIT the checkout, and
    /// telling it "reading … is not available" would be a sentence about the wrong thing sent to a model
    /// that just broke the read-only promise; an <c>mcp</c> denial is a tool this launch never meant to
    /// offer. Any word not named here ends the turn on its empty answer, as it did before epic 1 — and
    /// epic 2 classifies it. A new word earns a place here by being measured, not by being seen.</para>
    /// <para>A denied READ is not a shell: telling that model "shell commands are not available" would
    /// answer a question it did not ask and leave the one it did. Its wording has NOT been measured the way
    /// the command text was — it is the measured text's shape with the permission swapped, and says "from
    /// what you have already read" because a model refused a read is not helped by being told to read more.
    /// When a command was denied beside a read, the command text wins: it is the measured one.</para>
    /// </remarks>
    public static string For(IReadOnlyList<string> denied) =>
        denied.Contains(CommandAction, StringComparer.Ordinal)
            ? NoCommands
            : denied.FirstOrDefault(word => ReadActions.Contains(word, StringComparer.Ordinal)) is { } read
                ? NoReading(read)
                : string.Empty;

    /// <summary>
    /// What a silent agy launch was, read from the permission words it was denied: a command, a read, or
    /// <paramref name="unexplained"/> — the same allowlist <see cref="For"/> answers from, with the same precedence.
    /// </summary>
    /// <remarks>Moved here from <c>ConsultFailures</c>, which read these words for every vendor (the whole-branch review, F).</remarks>
    public static ConsultFailure Failure(IReadOnlyList<string> denied, ConsultFailure unexplained) =>
        denied.Contains(CommandAction, StringComparer.Ordinal)
            ? new ConsultFailure.CommandDenied()
            : denied.FirstOrDefault(word => ReadActions.Contains(word, StringComparer.Ordinal)) is { } read
                ? new ConsultFailure.ReadDenied(read)
                : unexplained;

    private static string NoReading(string action) =>
        $"Reading outside this checkout is not available in this consultation: the '{action}' permission was denied "
        + $"and {StaysDenied}. Do not call it again, and do not plan, schedule or delegate the work. Answer now, in "
        + "plain prose, from what you have already read with view_file. If a check needs what was denied, name it "
        + "for the caller to do.";
}
