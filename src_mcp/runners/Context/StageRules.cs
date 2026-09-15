namespace CoaiMcp.Runners.Context;

/// <summary>
/// Which written rules a gate with NO diff is judged against, in priority order.
/// </summary>
/// <remarks>
/// <para>The plan and document stages have no change to select from — they review a document — so the
/// rules they are held to have to be NAMED rather than derived. Both lists are orders, not sets: the
/// corpus plus the instruction files is larger than the byte budget, so an unordered set would be cut
/// by whatever sorted first, which is the 2026-09-06 starvation wearing a different alphabet.</para>
/// <para><b>A tier entry is checked twice before it is added</b> — that it exists in the corpus this
/// repository PINS (not in the conventions checkout on somebody's machine), and that it governs what
/// this stage actually reviews. A dead entry is a lie in a table whose whole job is to be read, and a
/// live but inapplicable one spends budget a relevant rule needed.
/// <see cref="StageRulesTests"/> keeps the first half honest against the real mount.</para>
/// <para><b>Why <c>rule-ownership.md</c> is absent</b>, though it is mounted and its tasks include
/// <c>docs</c>: its own frontmatter scopes it to <c>common/*.md</c>, <c>csharp/*.md</c>,
/// <c>rust/*.md</c>, <c>typescript/*.md</c> — the files of a shared-RULE repository — and its test is
/// "would another repository still need this sentence". A document round here judges a
/// <c>research/module_*.md</c> or a plan, which is not a shared rule. The RESOLVER may well select it
/// (epic 2) when the document under review IS a rule; that is the difference between a static tier,
/// which is what a stage is ALWAYS judged against, and a selection, which is what one change happens
/// to touch.</para>
/// </remarks>
public static class StageRules
{
    /// <summary>
    /// The plan gate: what makes a PLAN good, in the order they are worth spending budget on.
    /// </summary>
    /// <remarks>
    /// <c>security.md</c> leads for the reason it leads in <see cref="RuleOrder"/>'s walk — a missed
    /// security finding is the most expensive thing this gate can fail to say, and it is small. Then
    /// the three a plan is most often wrong about: building what already exists, filing the plan
    /// where it cannot be found, and a test plan that proves nothing. <c>development-workflow.md</c>
    /// is last because it is 14 KB and the most likely to be cut; <c>git-workflow.md</c> is absent
    /// because a plan round reviews a document, not a commit.
    /// </remarks>
    public static readonly IReadOnlyList<string> Plan =
    [
        "common/security.md",
        "common/reuse-first.md",
        "common/planning-docs.md",
        "common/testing.md",
        "common/coding-style.md",
        "common/knowledge-base.md",
        "common/development-workflow.md",
    ];

    /// <summary>
    /// The document gate: what makes a DOCUMENT right — where it lives, what it must carry, and how
    /// a reviewer is meant to read one.
    /// </summary>
    public static readonly IReadOnlyList<string> Document =
    [
        "common/knowledge-base.md",
        "common/planning-docs.md",
        "common/coai-document-gate.md",
    ];
}
