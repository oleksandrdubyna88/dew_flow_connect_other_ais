namespace CoaiMcp.Core.Commands;

/// <summary>What the operator has switched on, at the moment of this call.</summary>
/// <param name="Autonomous">Work without interrupting the person until there is no other way.</param>
/// <param name="SplitPlan">Break an accepted plan into epics and stories before building it.</param>
/// <param name="SplitWithFable">
/// Do the splitting — and the risky stories — with the caller's strongest model; which one is
/// <see cref="CommandContext.Models"/>. (Named for Fable, which every caller was told until issue #117.)
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
    bool FirstPlanRound = true)
{
    /// <summary>
    /// The two models the model order names — already resolved for THIS caller's kind by
    /// <see cref="CommandModels.For"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to the Claude Code pair, so a context built without it says exactly what every
    /// release before issue #117 said.
    /// </remarks>
    public ModelPair Models { get; init; } = CommandModels.ClaudeCode;

    /// <summary>How often the split work comes back through the gate — once per epic unless told otherwise.</summary>
    public GateScope GatePer { get; init; } = GateScope.Epic;
}

/// <summary>How often split work is gated (issue #131). Never per story: that cost two rounds a story.</summary>
public enum GateScope
{
    /// <summary>One plan round and one code round per epic; the epics stack; one commit each.</summary>
    Epic,

    /// <summary>One code round over the whole task at the end; still one commit per epic.</summary>
    Task,
}

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
            commands.Add(OrdersSplit(context) ? SplitCommand(context) : AlreadySplitCommand(context.GatePer));
        }
        if (OrdersSplit(context) && context.SplitWithFable)
        {
            commands.Add(ModelCommand(context.Models));
        }
        if (context.Autonomous)
        {
            commands.Add(AutonomyCommand(context));
        }

        return commands;
    }

    /// <summary>
    /// The words every split order's gate ending carries, whichever size and scope it is.
    /// </summary>
    /// <remarks>
    /// Public for the test that holds <see cref="OrdersSplit"/> and <see cref="For"/> to one event. It
    /// was "After EVERY story" until issue #131 — the words that made every story cost a branch, a plan
    /// round and a code round, because a gate session ends at its code round.
    /// </remarks>
    public const string GateOrderMarker = "THE GATE runs once";

    /// <summary>
    /// Split the work before building it, and say how often it comes back through the gate.
    /// </summary>
    /// <remarks>
    /// The size is stated WITH the numbers it was computed from, so the AI can disagree in writing
    /// rather than silently: a heuristic that hides its inputs cannot be argued with. The numbers of
    /// epics and stories are the owner's (issue #131), and "never more" is there because the AI read
    /// the old "2-4 × 2-4" as a floor and cut everything into four or five of each.
    /// </remarks>
    private static string SplitCommand(CommandContext context)
    {
        var shape = PlanShapeReader.Of(context.PlanText);

        return $"{Judgement(shape.Verdict)} "
            + $"(Measured from the plan you sent: {shape.Numbers} — size {shape.Verdict}. That is a "
            + "heuristic — if it is wrong for this plan, say so in your summary and do what is right.) "
            + GateEnding(context.GatePer, HasEpics(shape.Verdict));
    }

    private static string Judgement(PlanShape.Split size) => size switch
    {
        PlanShape.Split.Small =>
            "Split this plan into 3-5 logically complete STORIES — no epics. Fewer is fine when the work is smaller; never more.",
        PlanShape.Split.Medium =>
            "Split this plan into 2-3 EPICS, each of 2-3 logically complete STORIES. Fewer is fine when the work is smaller; never more.",
        PlanShape.Split.Large =>
            "Split this plan into 3-4 EPICS, each of 3-4 logically complete STORIES. Fewer is fine when the work is smaller; never more.",
        PlanShape.Split.Huge =>
            "Split this plan into 4-5 EPICS, each of 3-5 logically complete STORIES. Fewer is fine when the work is smaller; never more.",
        _ => "This plan is small enough to build as it stands; split it only if you disagree, and say why.",
    };

    private static bool HasEpics(PlanShape.Split size) => size >= PlanShape.Split.Medium;

    /// <summary>How often the work comes back through the gate — never per story.</summary>
    private static string GateEnding(GateScope scope, bool epics) => (scope, epics) switch
    {
        (GateScope.Epic, true) =>
            $"{GateOrderMarker} per EPIC, never per story: give each epic its own branch, starting from the "
                + "previous epic's commit; call review_plan with that epic's plan; build all of its stories "
                + "without gating them one by one; then ONE review_code over the epic's whole diff, with the "
                + "previous epic's commit as baseRef; resolve every finding, fix what you accepted, update the "
                + "documentation and the tests, and commit the epic as ONE commit. Only then start the next epic.",
        (GateScope.Task, true) =>
            $"{GateOrderMarker} for the WHOLE task, never per epic or story: this plan round was its plan "
                + "gate; build every epic and its stories on this branch without gating them, and commit each "
                + "epic as ONE commit as you finish it; then ONE review_code over the whole task's diff; resolve "
                + "every finding, fix what you accepted, update the documentation and the tests, and commit.",
        _ =>
            $"{GateOrderMarker} for this work, never per story: build it on this branch without gating each "
                + "piece; then ONE review_code over the whole diff; resolve every finding, fix what you "
                + "accepted, update the documentation and the tests, and commit it as ONE commit.",
    };

    /// <summary>
    /// From the second plan round on: this plan is a PIECE of a split, not a plan to split again.
    /// </summary>
    /// <remarks>
    /// Without this the loop has no floor. A plan is split into epics; each epic comes back for its
    /// own plan review, which is the right thing to do; and the gate, having no memory of the first
    /// order, tells it to split into epics again. The operator saw it before it could happen — and
    /// it is why the split order is a once-per-session thing rather than a per-round one. Under one
    /// gate for the whole task a piece should not come back at all, and is told so (issue #131).
    /// </remarks>
    private static string AlreadySplitCommand(GateScope scope) => scope == GateScope.Task
        ? "This plan is a PIECE of a split that is already under way, so do NOT split it again — and it is "
            + "not gated on its own: the task is gated once as a whole. Build it on the task's branch, commit "
            + "it as ONE commit, and leave the review to the single code round at the end of the task."
        : "This plan is a PIECE of a split that is already under way, so do NOT split it again: build "
            + "it as one unit, review its diff through this gate, fix, document, test and commit. If "
            + "it is genuinely too big for one unit, say so in your summary and say what you would "
            + "have cut it into — but do not start a second round of splitting on your own.";

    /// <summary>
    /// The words every model order opens with, whichever models it names.
    /// </summary>
    /// <remarks>
    /// The bench recognises the order by these words and holds its OWN copy — it drives the published
    /// server and references nothing here — so both copies are asserted against
    /// <c>shared/command-models.json</c>'s <c>orderOpensWith</c>, a file neither owns. It looked for
    /// "Fable" until the models became a per-caller choice (issue #117).
    /// </remarks>
    public const string ModelOrderMarker = "Do the SPLIT itself with ";

    /// <summary>Which model does which half — the caller's OWN two, per its kind.</summary>
    /// <remarks>
    /// <para>One template, and its Claude Code instance is the sentence every release before issue
    /// #117 sent, byte for byte: a person who configured nothing cannot tell the feature landed. A
    /// test holds it to the old text by equality.</para>
    /// <para>A slot with no name reads as generic words rather than as a gap — never "with  at its
    /// highest", and never another vendor's model.</para>
    /// </remarks>
    private static string ModelCommand(ModelPair models)
    {
        var strongest = CommandModels.NamedOr(models.Strongest, "the strongest model your client offers");
        var implementation = CommandModels.NamedOr(models.Implementation, "your usual model");

        return $"{ModelOrderMarker}{strongest} at its highest available version — deciding what the epics "
            + "and stories are is the judgement that shapes everything after it. Then implement: "
            + $"ordinary stories on {implementation}, and anything where being wrong is expensive — payments, "
            + $"money, authentication, security, architecture, data migration — on {strongest} (max) again. "
            + "Name the model you used for each story in your summary.";
    }

    /// <summary>
    /// What "autonomous" is made of, and when to interrupt the person.
    /// </summary>
    /// <remarks>
    /// <para>The six numbered orders are the operator's, 2026-09-05: <i>"эта галочка должна говорить не
    /// просто работать автономно, а давать чёткие инструкции"</i>. An AI told only to work
    /// autonomously fills in its own idea of the word, and the idea that gets filled in is the
    /// cheapest one — skip the tests, skip the docs, ship. Each order is one the operator had to give
    /// by hand that week.</para>
    /// <para>The "re-read the epics" clause is conditional, because early in a task there may be no
    /// epics yet and an instruction that cannot be carried out is one an AI has to interpret. Raised
    /// in this change's plan round.</para>
    /// </remarks>
    private static string AutonomyCommand(CommandContext context)
    {
        var scope = context.SplitPlan
            ? "re-read every epic and story you have written so far"
            : "re-read the whole plan";

        return "Work AUTONOMOUSLY. Say that you are working autonomously, and keep saying what you "
            + "are writing right now as you go. Autonomous means these orders, not a mood: "
            + "(1) every bug and every problem spot gets a RED-GREEN-RED test — a failing test first, "
            + "then the fix, then the test green, then a check that the test really fails without the "
            + "fix; (2) the documentation is updated with every change — the README, the manifest if "
            + "there is one, the module docs and every other file that describes what you changed; "
            + "(3) before any release, run ALL the tests that exist, not only the ones near your change; "
            + "(4) if the repository has a release or pull request process, do it — and on a pull "
            + "request, wait about five minutes, then check whether automatic comments appeared, read "
            + "them and fix what they name; (5) if a deploy triggers automatically, verify the result "
            + "against the target environment — dev, stage or test — and read its logs for errors "
            + "before calling it done; (6) re-read your own code against the repository's rules and "
            + "follow them, not only the ones you remember. "
            + "A question that does not block you is not asked now: write it down and put every one "
            + "of them at the END of your final summary. A question that DOES block you is asked at "
            + $"once — but before you ask it, {scope} and gather every other blocking question you can "
            + "foresee, so the person is interrupted once with all of them rather than repeatedly "
            + "with one. Where you can proceed under a stated assumption, do that instead of asking, "
            + "and say what you assumed.";
    }
}
