using System.Text.RegularExpressions;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The prompt a reviewer is handed: its role's text, what it has to work with, and the finding
/// contract — composed in one place, by the only code that knows which of those it is.
/// </summary>
// `partial` for exactly one reason: to host a source-generated regex. Native AOT cannot compile a
// regex at runtime, so [GeneratedRegex] does it at build time and needs a partial method to fill in.
internal sealed partial class ReviewerPrompt(RolePrompts prompts)
{
    private readonly RolePrompts _prompts = prompts;

    internal string ComposePrompt(PromptChoice choice, string context, bool hasCheckout) =>
        $"{WithoutTheStaleClaim(_prompts.ForChoice(choice))}\n\n{WhatYouHave(hasCheckout)}\n\n## The finding contract\n\nReturn ONLY a JSON object matching this schema — no fences, no prose:\n\n{FindingSchema.Json}\n\n{context}";

    /// <summary>
    /// What the reviewer actually has — said once, by the only code that knows which it is.
    /// </summary>
    /// <remarks>
    /// <para>Eighteen of the twenty-five shipped prompts opened with "You have the checkout
    /// read-only and the diff below", unconditionally — and the DEFAULT mode hands a code reviewer
    /// an empty temp directory and no checkout at all. So the product told the model a repository
    /// was there, the model went to look, and an agentic CLI running headless has nobody to ask for
    /// the permission a shell command needs: it refused itself and returned NOTHING. Measured
    /// 2026-09-06 over 71 antigravity reviewer runs: 15 of them, 21 %, all at that same wall, on two
    /// different reasoning efforts.</para>
    /// <para>The claim could never live in the prompt FILES, because there it can only ever be one
    /// of the two truths — and a prompt somebody has overridden in the catalog needs the sentence
    /// just as much as a shipped one, which is the second reason it is here.</para>
    /// </remarks>
    /// <summary>
    /// The old claim, removed from a prompt that still carries it.
    /// </summary>
    /// <remarks>
    /// <para>The prompt catalog is EDITABLE: a prompt somebody overrode before 2026-09-06 sits in
    /// their own data directory still opening with "You have the checkout read-only and the diff
    /// below", and no edit to the shipped files can reach it. Composing the true sentence underneath
    /// it hands the model two opposite instructions in one prompt — which is worse than either
    /// sentence alone, and is the shape that made a headless CLI go looking for a tool in the first
    /// place. Raised by gemini at the plan gate of the change that removed the claim, and again by
    /// the local reviewer at its code gate.</para>
    /// <para>Only that one sentence is removed. A person's own prompt is theirs; this strips the
    /// line the product used to put in it and nothing else.</para>
    /// </remarks>
    internal static string WithoutTheStaleClaim(string prompt)
    {
        var stripped = StaleClaim().Replace(prompt, string.Empty).TrimStart();

        // An override consisting ONLY of that sentence would become an empty prompt, and an empty
        // prompt to a reviewer is worse than a contradictory one: it produces the silent empty
        // answer this whole change exists to stop. Keep their text and let the composed sentence
        // disagree with it — visible beats blank. (Raised at the code gate, 2026-09-06.)
        return stripped.Length > 0 ? stripped : prompt;
    }

    // ANCHORED, and only to horizontal whitespace. Both halves were bought at the same gate round:
    // unanchored, "I know you have the checkout read-only and the diff below." inside somebody's own
    // sentence would be stripped and leave "I know"; with \s* instead of [ \t]*, a claim standing
    // between two paragraphs took a paragraph separator with it. So the match must begin a line or
    // follow a sentence end, and it may only eat the indentation in front of itself. The \s+ BETWEEN
    // the words stays — that is what tolerates the line wrap the prompt files were written with.
    // The TAIL matters as much: eat the line's own terminator when the claim stood on its own line,
    // otherwise eat the space after it. Without that, removing a mid-sentence claim left two spaces
    // and removing a whole line left a blank one.
    [GeneratedRegex(
        @"(?:(?<=^)|(?<=[.!?][ \t]))[ \t]*You\s+have\s+the\s+(?:repository\s+)?checkout\s+read-only\s+and\s+the\s+diff\s+below\.(?:[ \t]*\r?\n)?[ \t]*",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex StaleClaim();

    internal static string WhatYouHave(bool hasCheckout) =>
        hasCheckout
            ? "## What you have\n\nA READ-ONLY checkout of the repository in your working "
                + "directory, and the material below. Review the change, not the codebase."
            : "## What you have\n\nThe material below — the change, the plan and this "
                + "project's written rules — and NOTHING else. There is no checkout in your working "
                + "directory and no tool you can call: do not try to run a command, list a directory "
                + "or read a file. Answer from what is here.\n\nThat is deliberate: a reviewer "
                + "given the change alone finds more of what matters than one sent exploring a "
                + "repository.";
}
