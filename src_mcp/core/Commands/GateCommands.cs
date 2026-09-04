namespace CoaiMcp.Core.Commands;

/// <summary>What the operator has switched on, at the moment of this call.</summary>
/// <param name="Autonomous">Work without interrupting the person until there is no other way.</param>
/// <param name="SplitPlan">Break an accepted plan into epics and stories before building it.</param>
/// <param name="SplitWithFable">
/// Do the splitting — and the risky stories — with Fable.
/// <para>The switch is the WHOLE decision, and there is deliberately no second condition beside it.
/// It once asked whether a Fable REVIEWER was configured, on the reasoning that a command must never
/// name a model this machine has not got. The reasoning was sound and the premise was wrong: Fable
/// is not a reviewer here, it is a model of the AI that CALLED us, which already has it. Nobody
/// configures Fable as a vendor in this panel and nobody should — so the check was false on every
/// real machine and the switch was inert. Corrected by the operator.</para>
/// </param>
/// <param name="PlanText">The plan under review, for the split verdict. Empty outside a plan round.</param>
/// <param name="PlanStage">
/// Whether this is the PLAN gate. A code round has a diff and no plan, so a split verdict computed
/// there would be a number invented from source — raised twice in this change's plan round.
/// </param>
/// <param name="FirstPlanRound">
/// Whether this is the first plan round of this session. The order to SPLIT is given ONCE: an epic
/// that comes back for its own plan review is already the product of a split, and telling it to
/// split again is a loop with no floor — epics of epics, for ever. Raised by the operator before it
/// could happen.
/// </param>
public sealed record CommandContext(
    bool Autonomous = false,
    bool SplitPlan = false,
    bool SplitWithFable = false,
    string PlanText = "",
    bool PlanStage = false,
    bool FirstPlanRound = true);

/// <summary>
/// The orders a round hands back with its verdict.
/// </summary>
/// <remarks>
/// <para>The gate answers one question — are these findings gating, may you proceed — and the AI
/// that called it decides everything else: whether to split the work, when to interrupt the person,
/// which model to use for what. Those three are the OPERATOR's decisions, and the panel is where the
/// operator sits, so they travel back as instructions rather than as settings nobody downstream can
/// see.</para>
/// <para>Pure, so the wording is a test rather than something read off a screen. Every switch is off
/// by default and an empty list is exactly the behaviour of every release before this one.</para>
/// </remarks>
public static class GateCommands
{
    /// <summary>The sentence that introduces them, so an AI knows what it is reading.</summary>
    public const string Preamble =
        "COMMANDS from the operator of this gate. They come from switches a person set in the panel "
            + "and they outrank your own defaults for this task. Follow them, and say in your summary "
            + "which ones you applied.";

    /// <summary>
    /// Whether this call actually ORDERS a split — the one thing the caller has to remember.
    /// </summary>
    /// <remarks>
    /// Public because the server records the order and must record exactly what was given, never a
    /// second copy of the same condition. Two copies of one question is how the surface-name check
    /// ended up with three, one of which refused every leg of a measured arm.
    /// </remarks>
    public static bool OrdersSplit(CommandContext context) =>
        context.SplitPlan && context.PlanStage && context.FirstPlanRound;

    /// <summary>The orders for this call, in the order they are meant to be carried out.</summary>
    public static IReadOnlyList<string> For(CommandContext context)
    {
        var commands = new List<string>();
        if (context.SplitPlan && context.PlanStage)
        {
            commands.Add(OrdersSplit(context) ? SplitCommand(context) : AlreadySplitCommand);
        }
        if (OrdersSplit(context) && context.SplitWithFable)
        {
            commands.Add(FableCommand);
        }
        if (context.Autonomous)
        {
            commands.Add(AutonomyCommand(context));
        }

        return commands;
    }

    /// <summary>
    /// Split the work before building it, and close every piece properly.
    /// </summary>
    /// <remarks>
    /// The verdict is stated WITH the numbers it was computed from, so the AI can disagree in
    /// writing rather than silently: a heuristic that hides its inputs cannot be argued with.
    /// </remarks>
    private static string SplitCommand(CommandContext context)
    {
        var shape = PlanShapeReader.Of(context.PlanText);
        var judgement = shape.Verdict switch
        {
            PlanShape.Split.Epics =>
                "Split this plan into 2-4 EPICS, then each epic into 2-4 logically complete STORIES.",
            PlanShape.Split.Stories =>
                "Split this plan into 2-4 logically complete STORIES. It is not broad enough to need epics.",
            _ => "This plan is small enough to build as it stands; split it only if you disagree, and say why.",
        };

        return $"{judgement} "
            + $"(Measured from the plan you sent: {shape.Numbers}. That is a heuristic — if it is wrong "
            + "for this plan, say so in your summary and do what is right.) "
            + "After EVERY story: call review_code on that story's diff, resolve every finding, fix "
            + "what you accepted, update the documentation and the tests, and commit. Only then start "
            + "the next one. A story that is not reviewed, documented, tested and committed is not "
            + "finished.";
    }

    /// <summary>
    /// From the second plan round on: this plan is a PIECE of a split, not a plan to split again.
    /// </summary>
    /// <remarks>
    /// Without this the loop has no floor. A plan is split into epics; each epic comes back for its
    /// own plan review, which is the right thing to do; and the gate, having no memory of the first
    /// order, tells it to split into epics again. The operator saw it before it could happen — and
    /// it is why the split order is a once-per-session thing rather than a per-round one.
    /// </remarks>
    private const string AlreadySplitCommand =
        "This plan is a PIECE of a split that is already under way, so do NOT split it again: build "
            + "it as one unit, review its diff through this gate, fix, document, test and commit. If "
            + "it is genuinely too big for one unit, say so in your summary and say what you would "
            + "have cut it into — but do not start a second round of splitting on your own.";

    /// <summary>Which model does which half, when Fable is here.</summary>
    private const string FableCommand =
        "Do the SPLIT itself with Fable at its highest available version — deciding what the epics "
            + "and stories are is the judgement that shapes everything after it. Then implement: "
            + "ordinary stories on Opus, and anything where being wrong is expensive — payments, "
            + "money, authentication, security, architecture, data migration — on Fable (max) again. "
            + "Name the model you used for each story in your summary.";

    /// <summary>
    /// When to interrupt the person, and when not to.
    /// </summary>
    /// <remarks>
    /// The "re-read the epics" clause is conditional, because early in a task there may be no epics
    /// yet and an instruction that cannot be carried out is one an AI has to interpret. Raised in
    /// this change's plan round.
    /// </remarks>
    private static string AutonomyCommand(CommandContext context)
    {
        var scope = context.SplitPlan
            ? "re-read every epic and story you have written so far"
            : "re-read the whole plan";

        return "Work AUTONOMOUSLY. A question that does not block you is not asked now: write it down "
            + "and put every one of them at the END of your final summary. A question that DOES block "
            + $"you is asked at once — but before you ask it, {scope} and gather every other blocking "
            + "question you can foresee, so the person is interrupted once with all of them rather "
            + "than repeatedly with one. Where you can proceed under a stated assumption, do that "
            + "instead of asking, and say what you assumed.";
    }
}
