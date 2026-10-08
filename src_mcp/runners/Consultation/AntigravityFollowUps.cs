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

    /// <summary>How every lookup continuation begins — and the first prompt never does, so the turns can be told apart.</summary>
    public const string LookupHeadingStart = "## coai looked it up";

    /// <summary>The continuation's heading, with its turn number: <c>{0}</c> is the turn, and the total follows.</summary>
    /// <remarks>Plain ASCII on purpose: the message travels as JSON on agy's stdin, where a non-ASCII dash may arrive as
    /// <c>—</c> and a reader matching the raw line would never see the turn.</remarks>
    public const string LookupTurnMarker = LookupHeadingStart + " - turn {0} of";

    /// <summary>What the last allowed turn is told — a block on it is not served.</summary>
    public const string LastTurn = "This is your LAST turn";

    /// <summary>
    /// What a consultant that may ask coai to look is told it has (todo/PLAN_agy_searches_through_coai.md §3): the block,
    /// its limits, and where it may look — never the continuation's heading, which only coai's reply carries.
    /// </summary>
    /// <param name="roots">The granted roots, absolute — a path the model writes must be inside one.</param>
    /// <param name="followUps">How many times coai will answer a block.</param>
    public static string LookupToolbox(IReadOnlyList<string> roots, int followUps) =>
        "You cannot list a folder or search inside one yourself, and shell commands are refused. coai can do both for "
        + "you, read-only: end your answer with a block like this one (at most "
        + $"{Core.Feature.LookupBudget.RequestsPerTurn} lines):\n\n"
        + "```" + Core.Consultation.LookupRequests.Fence + "\n"
        + "list <folder>\n"
        + "search \"<text>\" in <folder>\n"
        + "```\n\n"
        + "The text is matched literally, ignoring case — not a pattern. A folder is an absolute path inside "
        + (roots.Count == 1 ? $"{roots[0]} (or a path relative to it)" : "one of: " + string.Join(", ", roots))
        + $". coai answers in this same conversation, up to {followUps} time{(followUps == 1 ? string.Empty : "s")}; "
        + "then answer in prose, with no block. Open what it finds with view_file.";

    /// <summary>
    /// What a refused permission's follow-up adds when coai may look for this consultant — after the measured text, never
    /// instead of it: a model refused the shell is the one that needs a listing (todo/PLAN_agy_searches_through_coai.md §3).
    /// Not measured on its own; the live record is RESULTS_agy_searches_through_coai.md.
    /// </summary>
    public const string AskCoaiToLook =
        "If you need a folder listed or searched first, end your answer with a " + Core.Consultation.LookupRequests.Fence
        + " block as described in the first message, and coai will do it for you, read-only.";

    /// <summary>The message a lookup continuation sends: what coai served and refused, and what to do next.</summary>
    /// <param name="turn">The turn this message opens, 2 for the first continuation.</param>
    /// <param name="turns">The most turns this answer may take.</param>
    /// <param name="served">What coai's reader returned for the block's requests.</param>
    /// <param name="refused">The block's lines that were not requests, each with why.</param>
    public static string LookupContinuation(int turn, int turns, LookupServed served, IReadOnlyList<string> refused)
    {
        var text = new System.Text.StringBuilder()
            .Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, LookupTurnMarker, turn)).Append(' ').Append(turns).Append("\n\n");
        if (served.Text.Length > 0)
        {
            text.Append(served.Text).Append("\n\n");
        }

        foreach (var line in refused)
        {
            text.Append("not served: ").Append(line).Append('\n');
        }

        return text.Append(turn >= turns
                ? $"\n{LastTurn}: answer now, in plain prose, from what you have read. A {Core.Consultation.LookupRequests.Fence} block will not be served."
                : $"\nAnswer now in plain prose — or, if you still need to look, end your answer with another {Core.Consultation.LookupRequests.Fence} block.")
            .ToString();
    }

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
