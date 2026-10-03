using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// How a vendor remembers a conversation. A closed union, so a vendor whose resume has not been
/// measured is <see cref="WeRemember"/> rather than a guess — and changing one field is all it
/// costs to move it once it has been.
/// </summary>
public abstract record ConsultantMemory
{
    /// <summary>The vendor stores the conversation; we send a handle and nothing else.</summary>
    public sealed record VendorRemembers : ConsultantMemory;

    /// <summary>The vendor stores nothing; WE carry the transcript into every turn, bounded.</summary>
    public sealed record WeRemember(int CarryBudget) : ConsultantMemory;

    private ConsultantMemory() { }
}

/// <summary>Everything one consultation turn needs to become a process.</summary>
/// <param name="RepoPath">The LIVE checkout the consultant runs in, read-only.</param>
/// <param name="Handle">The vendor's own conversation id — empty for a fresh consultation.</param>
/// <param name="OutputDir">Where the vendor may write its answer file; never inside the repository.</param>
/// <param name="AnswerSchemaFile">
/// Where the answer schema already sits, for the one route that insists on one. Provisioned by the
/// service, never by <c>Build</c> — a pure builder is what makes every flag a unit test.
/// </param>
public sealed record ConsultantLaunch(
    string RepoPath,
    string Prompt,
    string Handle,
    string OutputDir,
    ReviewerSettings Settings,
    string AnswerSchemaFile = "")
{
    /// <summary>
    /// How this launch is confined. <see cref="LaunchConfinement.AsShipped"/> by default, so every
    /// launch written before the question consultant builds today's argv byte for byte; a question row
    /// carries a <see cref="LaunchConfinement.Planned"/> and the adapter composes the plan.
    /// </summary>
    public LaunchConfinement Confinement { get; init; } = LaunchConfinement.AsShipped;

    /// <summary>
    /// An EMPTY directory for a planned launch whose plan stands in <c>Scratch</c> — created by the
    /// caller, never by <c>Build</c>, which is pure. Required then, ignored otherwise.
    /// </summary>
    public string ScratchDir { get; init; } = string.Empty;
}

/// <summary>What <see cref="IConsultantRuntime.PrepareAsync"/> concluded: a launch ready to build, or a refusal.</summary>
/// <remarks>
/// A closed pair rather than a launch that might be unsafe: the one way a prepared claude launch can go wrong is a
/// CLI whose <c>--help</c> never answered, and the review of epic 3 ruled that such a turn is REFUSED before it is
/// launched — never downgraded to an argv without <c>--restricted</c>. Refusing is a value the service applies with
/// epic 2's failure machinery, not an exception.
/// </remarks>
public abstract record ConsultantPreparation
{
    /// <summary>Ready to build.</summary>
    /// <param name="Confinement">
    /// What this launch will be SENT, as <c>shared/consultant-limitations.json</c> qualifies it — <c>restricted</c> or
    /// <c>no-restricted</c> for claude, empty for a vendor whose confinement does not depend on its CLI's version.
    /// Recorded on the turn, so a later reader shows what ran rather than what a fresh probe says now.
    /// </param>
    /// <param name="Note">Why, for the log — empty when there was nothing to learn.</param>
    public sealed record Ready(ConsultantLaunch Launch, string Confinement, string Note) : ConsultantPreparation;

    /// <summary>Not launched, and why — classified, with its cure.</summary>
    public sealed record Refused(ConsultFailure Failure) : ConsultantPreparation;

    private ConsultantPreparation() { }
}

/// <summary>The roles a consultation launch carries — strings, since roles became data.</summary>
public static class ConsultantRoles
{
    /// <summary>The stuck consultant: a conversation in the live checkout.</summary>
    public const string Consult = "consult";

    /// <summary>A question row: one shot, confined by a plan (PLAN_question_consultant.md).</summary>
    public const string Question = "question";

    /// <summary>
    /// The LEDGER role of a consultant check (<c>coai-mcp --check-consultant</c>): one paid turn of a configured
    /// consultant in a scratch repository, billed apart from the consultations a caller asked for.
    /// </summary>
    /// <remarks>The launch itself still carries <see cref="Consult"/> — it is the same turn a consultation makes.</remarks>
    public const string Check = "consult-check";
}

/// <summary>
/// What a consultation leaves in the answers directory, and how it is recognised again.
/// </summary>
/// <remarks>
/// <para>ONE place, because the adapter that writes the name and the sweep that deletes it live in
/// different projects and are read by different people. An ad-hoc substring check in the sweep would
/// either leak files the day an adapter renamed its output or delete something another component
/// happened to call the same thing. (gemini, story 2's second code round.)</para>
/// <para>Both shapes come from the role a consultation carries: <c>codex-consult-&lt;guid&gt;.txt</c>
/// from a CLI route, <c>local-consult-&lt;guid&gt;.prompt</c> and <c>.json</c> from the local one.</para>
/// </remarks>
public static class ConsultantArtefacts
{
    /// <summary>The infix every consultation artefact carries, because every one is named for the role.</summary>
    public const string Marker = "-" + ConsultantRoles.Consult + "-";

    /// <summary>The infix a question row's artefact carries — the same rule, the other role.</summary>
    public const string QuestionMarker = "-" + ConsultantRoles.Question + "-";

    private static readonly string[] Extensions = [".txt", ".prompt", ".json"];

    /// <summary>A file name this product wrote for a consultation or a question row.</summary>
    public static bool Ours(string fileName) =>
        (fileName.Contains(Marker, StringComparison.Ordinal) || fileName.Contains(QuestionMarker, StringComparison.Ordinal))
        && Array.Exists(Extensions, e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>A fresh artefact name for one launch. Unique, so two launches cannot share a file.</summary>
    public static string Name(string vendor, string extension, string role = ConsultantRoles.Consult) =>
        $"{Core.Rounds.FileName.Safe(vendor)}-{Core.Rounds.FileName.Safe(role)}-{Guid.NewGuid():N}{extension}";
}

/// <summary>
/// The conversation-shaped vendor adapter: the <c>ChatAdapter</c> seam of the extension, in C#.
/// </summary>
/// <remarks>
/// <para>Composed FROM a reviewer adapter rather than widening one: the launch it builds carries the
/// reviewer adapter as <see cref="ReviewerInvocation.Adapter"/>, so the answer envelope and the usage
/// arithmetic keep their one copy, while the argv — a different SHAPE per turn for codex, measured —
/// lives here. The launch itself goes through <c>ReviewerExecutor.LaunchAsync</c> unchanged.</para>
/// <para><c>Build</c> is pure: every flag is a unit test.</para>
/// <para><b>The coupling this buys, and what would end it.</b> Returning a
/// <see cref="ReviewerInvocation"/> means a consultant must have a reviewer adapter to delegate its
/// answer and usage reading to. Every vendor this product can consult HAS one — codex, claude,
/// antigravity and the local engine are all reviewers first — so the constraint costs nothing today,
/// and what it prevents is four copies of a token arithmetic that is wrong by a factor of two when it
/// drifts. It becomes wrong the day a CONSULT-ONLY vendor appears, with no findings schema and no
/// reason to be a reviewer: that is the trigger to split a <c>ConsultantInvocation</c> out of this,
/// and it is a change to one seam rather than to the adapters. Named by the gate's architecture
/// reviewer on story 1's code round, and deliberately not built ahead of the vendor that needs it.</para>
/// </remarks>
/// <para><b>Since the question consultant</b> (PLAN_question_consultant.md, S1) the launch half —
/// <c>Vendor</c>, <c>Build</c>, <c>NeedsAnswerSchema</c>, <c>ReadAdvice</c> — is the base interface
/// <see cref="IAnsweringRuntime"/>, which a one-shot question row is launched through; what stays
/// here is about the CONVERSATION, and no existing adapter changed shape.</para>
public interface IConsultantRuntime : IAnsweringRuntime
{
    ConsultantMemory Memory { get; }

    /// <summary>
    /// The launch with whatever this vendor must learn about the INSTALLED CLI before it is built — or the
    /// launch unchanged, which is what every vendor but one says.
    /// </summary>
    /// <remarks>
    /// <para>Exists so <see cref="IAnsweringRuntime.Build"/> can stay pure while the argv depends on the machine: claude 2.1.258
    /// accepts <c>--restricted</c> and 2.1.197 refuses the launch for it (measured 2026-10-02,
    /// research/RESULTS_claude_consultant_confinement.md), so the claude adapter asks its CLI here and hands
    /// the answer to <c>Build</c> as data. A defaulted member rather than a branch on the vendor in the
    /// service: the service calls it for every consultant, the vendor that has nothing to learn says
    /// nothing, and the next vendor whose flags depend on its version adds an override rather than an
    /// <c>if</c> in a file already past the size ceiling.</para>
    /// <para>Called once per turn, before <c>Build</c> — never cached (risk consultation 264fbcf2,
    /// 2026-10-03: a long-lived server's cached answer survives an in-place upgrade of the CLI). It may REFUSE
    /// (<see cref="ConsultantPreparation.Refused"/>) when what it had to learn could not be learned.</para>
    /// </remarks>
    Task<ConsultantPreparation> PrepareAsync(ConsultantLaunch launch, IProcessLauncher launcher, CancellationToken ct) =>
        Task.FromResult<ConsultantPreparation>(new ConsultantPreparation.Ready(launch, string.Empty, string.Empty));

    /// <summary>The vendor's own id for this conversation, read off whatever the process said — or empty.</summary>
    string ReadHandle(ProcessResult result);

    /// <summary>The vendor no longer holds this conversation: a resume that cannot be honoured.</summary>
    bool DroppedTheConversation(ProcessResult result);

    /// <summary>
    /// This vendor reports what the WHOLE conversation has consumed, not what this turn did.
    /// </summary>
    /// <remarks>
    /// True for antigravity, measured: turn 1 reported 14 138 input tokens and turn 2 reported
    /// 30 843, which is turn 1 plus turn 2. Recorded as a property of the ADAPTER rather than fixed
    /// inside it, because only the caller holds the running total to subtract — and a spending record
    /// that counts turn 1 twice is the one thing a spending record must not do. False everywhere
    /// else, which is why it is a defaulted member: a vendor that reports per turn says nothing.
    /// </remarks>
    bool UsageIsCumulative => false;

    /// <summary>
    /// A second launch that CONTINUES this turn's first, when the first answered nothing in a way only
    /// continuing can cure — or null, and the turn ends on what the first said.
    /// </summary>
    /// <remarks>
    /// <para>The consultation's half of issue #504. A headless agentic CLI that asks for a permission
    /// nobody can grant — a shell command, a read outside the checkout — has it auto-denied and ends its
    /// turn with nothing; asking the same question again meets the same denial, and only the SAME
    /// conversation can be told the permission will not come. Measured on agy 1.2.15, 2026-10-02: 6 of 6
    /// such follow-ups answered in prose (research/RESULTS_agy_consult_follow_up.md).</para>
    /// <para>Pure — it reads the finished launch — and defaulted, so a vendor whose turns cannot end this
    /// way says nothing. <c>ConsultantTurn</c> decides WHETHER a follow-up runs — the launch answered
    /// nothing, there is time, the tree is clean — and asks this only once the first of those holds; this
    /// decides only what the follow-up would be.</para>
    /// </remarks>
    ReviewerInvocation? FollowUp(ReviewerInvocation first, ReviewerLaunch launched) => null;

    /// <summary>The permission words this vendor reported denying in a finished launch — empty when none.</summary>
    /// <remarks>
    /// The vendor's own vocabulary, kept as it is (<c>command</c>, <c>read_file</c>): the follow-up names it
    /// back, and epic 2 of PLAN_the_consultant_works_on_every_vendor.md classifies a failed turn by it.
    /// </remarks>
    IReadOnlyList<string> DeniedActions(ReviewerLaunch launched) => [];

    /// <summary>What this vendor refused in a finished launch, each with WHAT it was asked for when the CLI says so.</summary>
    /// <remarks>
    /// <see cref="DeniedActions"/> with the target beside each word: a claude envelope names the file or pattern a refused
    /// tool was given; agy's <c>denied_actions</c> carry none, so its targets are empty — which the consultant check reports
    /// as a denial it cannot attribute (epic 4's code round).
    /// </remarks>
    IReadOnlyList<DeniedAction> Denials(ReviewerLaunch launched) => [.. DeniedActions(launched).Select(action => new DeniedAction(action, string.Empty))];

    /// <summary>
    /// What a launch that exited cleanly and answered NOTHING was, in this vendor's own words: a refusal its CLI recorded
    /// (<see cref="ConsultFailure.CommandDenied"/>, <see cref="ConsultFailure.ReadDenied"/>) — or <paramref name="unexplained"/>
    /// when it recorded none this adapter can name.
    /// </summary>
    /// <remarks>
    /// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), finding F. The
    /// classification read agy's permission words (<c>command</c>, <c>read_file</c>, <c>read_url</c>) for EVERY vendor, so a
    /// claude turn whose <c>permission_denials</c> showed a refused <c>Read</c> was classified <c>empty</c>. Each adapter
    /// now maps its own vocabulary; one that maps nothing says nothing, and the empty answer stands.
    /// </remarks>
    ConsultFailure SilentFailure(ReviewerLaunch launched, ConsultFailure unexplained) => unexplained;

    /// <summary>
    /// The executable whose own help must be asked before this vendor's argv can be built — the one its turn launches —
    /// or empty when its flags do not depend on the installed CLI.
    /// </summary>
    /// <remarks>
    /// What <c>--consultants</c> asks beside the version (claude: whether its <c>--help</c> declares <c>--restricted</c>),
    /// so the survey asks the adapter rather than comparing the runtime's name, and the executable it asks is the one
    /// <see cref="PrepareAsync"/> asks — one road, not two copies of the same default (the whole-branch review, F).
    /// </remarks>
    /// <param name="configuredExecutable">The row's executable path; empty for the runtime's own default.</param>
    string CapabilityExecutable(string configuredExecutable) => string.Empty;

    /// <summary>
    /// What the prompt tells this vendor about the tools it actually has — a sentence under
    /// "## What you have", or empty to say nothing.
    /// </summary>
    /// <remarks>
    /// Only tools OBSERVED working in the shipped launch mode are named. agy's <c>init</c> event declares
    /// <c>grep_search</c>, <c>find_by_name</c> and <c>list_dir</c>, and the model, asked to use them in
    /// <c>--mode plan</c>, said it does not have them (measured 2026-10-02) — a declaration is not
    /// evidence. Telling a model about a tool it lacks is how a turn ends in the delegation and the
    /// shell call this exists to avoid.
    /// </remarks>
    string Toolbox => string.Empty;
}

/// <summary>One refusal a CLI recorded: the permission or tool word, and what it was asked for — empty when the CLI does not say.</summary>
/// <param name="Action">The vendor's own word (<c>command</c>, <c>read_file</c>, <c>Read</c>).</param>
/// <param name="Target">The path or pattern the refused tool was given; empty when the CLI's record carries none.</param>
public sealed record DeniedAction(string Action, string Target);
