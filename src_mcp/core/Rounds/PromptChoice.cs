namespace CoaiMcp.Core.Rounds;

/// <summary>One prompt a reviewer can be given: an id, the role it serves, and what it is for.</summary>
/// <remarks>
/// <para>What the panel shows and what a round asks FOR. The text itself is not here: a prompt's
/// body lives in <c>src_mcp/src/prompts/&lt;id&gt;.md</c>, embedded in the binary, with an override
/// layer under the data directory — see <c>RolePrompts</c>.</para>
/// <para><b>The last twelve lenses were MEASURED before they were shipped, and the measurement
/// changed what shipped.</b> Each was drafted three times, and the three drafts turned out to be
/// three SHAPES rather than three wordings — a question list, a task to enact, a rule with
/// exceptions — held constant across all twelve. Seventy-two runs later
/// (<c>research/RESULTS_focused_prompts.md</c>): the shapes find the same AMOUNT (6.6–6.9 findings,
/// 79–82 % gating, flat) and differ in whether they find the same thing TWICE — 42 % against 32 %.
/// So a lens is written as a task to perform wherever the subject has a sequence to enact ("run it
/// twice, a millisecond apart, and narrate both"), and as a question list only where it does not.
/// Five of the twelve picks were decided by that measurement; seven were inside its noise and took
/// the shape result as a prior. <b>Anyone adding a lens should read that file first.</b></para>
/// <para>This record outlived the class it was declared in. <c>PromptCatalog</c> held the same
/// twenty-five rows as a C# array until 2026-09-12, when they became
/// <c>shared/builtin-roles.json</c> — a file neither half owns, embedded by this one and generated
/// into the extension. There is no second copy to keep level any more, which is why the class that
/// held one is gone and this is all that remains of its file.</para>
/// </remarks>
/// <param name="Id">The file name without its extension — what settings and the panel name.</param>
/// <param name="Universal">
/// True for the broad prompt of a role. Exactly one per role, and it is what a round uses when
/// nobody has chosen otherwise: a narrow lens is a deliberate act, never a default. The seed says so
/// by POSITION — a role's first prompt — which is what <c>RoleDefinition.General</c> reads.
/// </param>
/// <param name="BuiltIn">
/// Shipped in the seed and embedded in the binary — so it has a default text to restore, and it
/// cannot be deleted. A prompt a person added has neither. Trailing with a default so every
/// construction site written before the catalog was data keeps compiling and keeps meaning what it
/// meant.
/// </param>
public sealed record PromptChoice(string Id, string Role, string Label, string Purpose, bool Universal, bool BuiltIn = false);
