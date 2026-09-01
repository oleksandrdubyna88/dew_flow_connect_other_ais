namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What a code round must be told BEFORE it is shown a diff.
/// </summary>
/// <remarks>
/// <para>A reviewer given only a diff can say whether the code is defensible. It cannot say whether
/// the code is what was ASKED for — and those come apart constantly: a change can be well written,
/// well tested, and solve the wrong problem. Only the second question catches that, and only a
/// scope can ask it.</para>
/// <para>Nothing used to enforce this. <c>review_code</c> took the plan as an ordinary argument and
/// an empty one was accepted in silence, so the reviewer's whole job quietly narrowed to "is this
/// diff reasonable". The scope a plan round had already agreed on was not even kept between the
/// stages.</para>
/// </remarks>
public static class CodeScope
{
    /// <summary>
    /// Short enough to be a ticket title is not a scope.
    /// </summary>
    /// <remarks>
    /// The floor is deliberately low and the reason is honest: this cannot measure whether a scope
    /// is GOOD, only whether one was written. "fix the update button" passes any is-it-empty check
    /// and tells a reviewer nothing about what the change was for, which is precisely the question
    /// it is being asked. Two hundred characters is about the length at which a person stops
    /// naming the change and starts describing it.
    /// </remarks>
    public const int Floor = 200;

    public static bool IsSubstantial(string scope) => scope.Trim().Length >= Floor;

    /// <summary>What to tell a caller that sent no scope — an instruction, never just a refusal.</summary>
    public const string Refusal =
        "review_code needs the SCOPE of the change, not only its diff: pass planText describing what " +
        "this change was supposed to achieve — the symptom or goal, what must be true when it is " +
        "done, and the constraints. A reviewer given only a diff can judge whether the code is " +
        "defensible; it cannot judge whether the code is what was asked for, which is the question " +
        "this gate exists to answer. The plan you passed at the plan stage is kept and reused " +
        "automatically, so this normally needs nothing from you.";

    /// <summary>The same rule, said to a caller who is at the PLAN stage.</summary>
    public const string PlanRefusal =
        "review_plan needs an actual plan, not a title: say what the change is for \u2014 the symptom or " +
        "goal, what must be true when it is done, and the constraints. This text becomes the SCOPE " +
        "the code stage judges the diff against, so a plan accepted on two words would leave the " +
        "code reviewers with nothing to check the change against but the change itself.";
}
