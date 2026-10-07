using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Claude as a consultant: one process per turn, the conversation resumed by the session id the CLI
/// reports in its own JSON envelope.
/// </summary>
/// <remarks>
/// <para>Measured 2026-09-12 with the full review flag set and <c>--add-dir</c> pointed at a real
/// checkout: turn 1 in 5.4 s reporting its <c>session_id</c> unasked, <c>--resume</c> in 4.4 s
/// returning the number planted in turn 1.</para>
/// <para><b>The id is READ, not chosen.</b> <c>--session-id</c> exists and would let the server mint
/// one, which is tempting — and would make <c>Build</c> impure, since a fresh GUID per call is a
/// launch no test can assert. The CLI reports the id either way, so every consultant here has the
/// same shape: run, read the handle off what the process said, resume with it.</para>
/// <para><b>What it may do: three tools, named.</b> A consultant is asked to READ the tree, so it is
/// offered <c>Read</c>, <c>Glob</c> and <c>Grep</c> and nothing else — an ALLOWLIST, so a shell, an edit
/// tool, the network or a sub-agent is not denied but absent. `--permission-mode plan` is the CLI's
/// promise and a shell is a way around it (`rm`, `mv`, `sed -i`), which story 2's plan round named. That
/// story answered with a deny-list, and a deny-list is a list of what somebody thought of: it named
/// <c>Bash</c> and never <c>PowerShell</c>, so on Windows the consultant read a canary outside the
/// repository 6 of 6 and was offered the user's whole tool set — <c>Artifact</c>, <c>CronCreate</c>,
/// <c>SendMessage</c> … (2026-10-01, <c>RESULTS_question_consultant_capabilities.md</c> on branch
/// <c>feat/question-consultant</c>).</para>
/// <para><b>Where it may read: <c>--restricted</c>, when the installed CLI has it.</b> <c>--tools</c>
/// limits WHICH tools exist, not WHERE they read — on 2026-10-01 the allowlist alone still read outside
/// the working directory on Windows 2.1.258. <c>--restricted</c> confines the file tools to the working
/// directories and <c>--add-dir</c>: 9 of 9 cells confined, fresh and resumed, sign-in intact, on Windows
/// 2.1.258 (2026-10-02, research/RESULTS_claude_consultant_confinement.md, harness
/// scripts/probe-claude-consultant-confinement.mjs). claude 2.1.197 refuses the flag outright, so
/// <see cref="PrepareAsync"/> asks the installed CLI before every turn and <c>Build</c> drops the flag for
/// a CLI that lacks it — and that row of <c>shared/consultant-limitations.json</c> then says the consultant
/// is NOT confined to the repository, and to update claude.</para>
/// <para>These are requests to the CLI, and a unit test sees only that they are SENT; the probe is the
/// evidence that they take effect. The filesystem invariant remains the check behind all of this: a flag
/// is the vendor's promise, and the invariant is ours.</para>
/// </remarks>
public sealed class ClaudeConsultant(IReviewerRuntime inner, string vendor = "claude") : IConsultantRuntime
{
    /// <summary>
    /// The only tools a consultant is offered — ONE argument, comma-joined, the form the probe measured. The question
    /// consultant's disk allowlist, spelled once (<see cref="ConfinementPlanner.ClaudeDiskTools"/>): both measured the
    /// same three tools, and two copies of a confinement list are one edit from differing.
    /// </summary>
    private static readonly string ReadTools = string.Join(',', ConfinementPlanner.ClaudeDiskTools);

    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

    /// <summary>
    /// The cure a turn refused on an unknown capability carries — what the person can do about a help that never
    /// answered.
    /// </summary>
    public const string NoAnswerCure =
        "claude --help did not answer, so coai cannot tell whether --restricted is supported and will not launch it "
        + "unconfined — check the CLI (`claude --help`) or choose another consultant";

    /// <summary>
    /// The launch with <see cref="ReviewerSettings.ClaudeCli"/> read off the installed CLI's own help — or a refusal
    /// when the help never answered.
    /// </summary>
    /// <remarks>
    /// Every turn, never cached — <see cref="ClaudeCapability"/> says why, and what it costs. A help that did not
    /// come back after two asks says nothing about the flag, and the first version of this read it as "not declared"
    /// and launched unconfined (epic 3's code round, security); it is now <c>vendor-refused</c> with
    /// <see cref="NoAnswerCure"/>, and nothing is launched.
    /// </remarks>
    public async Task<ConsultantPreparation> PrepareAsync(ConsultantLaunch launch, IProcessLauncher launcher, CancellationToken ct)
    {
        var cli = await ClaudeCapability.ProbeAsync(launcher, Executable(launch.Settings), launch.RepoPath, ct);

        return cli switch
        {
            { Support: RestrictedSupport.Declared or RestrictedSupport.NotDeclared } =>
                new ConsultantPreparation.Ready(launch with { Settings = launch.Settings with { ClaudeCli = cli } }, cli.Qualifier, $"{cli.Qualifier}: {cli.Reason}"),
            // A CLI that could not be STARTED refused nothing: the cure is to install it or point the row at it (the
            // whole-branch review, L) — "check claude --help" would send a person to run a program that is not there.
            { CouldNotStart: true } => new ConsultantPreparation.Refused(new ConsultFailure.CliNotFound(cli.Reason, ConsultFailures.InstallCureFor("claude"))),
            _ => new ConsultantPreparation.Refused(new ConsultFailure.VendorRefused(cli.Reason, NoAnswerCure)),
        };
    }

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);

        return launch.Confinement is LaunchConfinement.Planned planned
            ? Planned(launch, planned.Plan)
            : AsShipped(launch);
    }

    /// <summary>The stuck consultant, as it ships: the read-tool allowlist (and <c>--restricted</c> where the CLI has it), the live checkout, resumable.</summary>
    private ReviewerInvocation AsShipped(ConsultantLaunch launch)
    {
        var request = Request(launch,
            [
                "-p",
                "--output-format", "json",
                "--permission-mode", "plan",
                .. RestrictedArgument(launch.Settings.ClaudeCli),
                "--tools", ReadTools,
                // No MCP server at all, like a reviewer (issue #514).
                NoMcpServers.ClaudeFlag,
                "--add-dir", launch.RepoPath,
                .. launch.Handle.Length > 0 ? (string[])["--resume", launch.Handle] : [],
                .. Model(launch.Settings),
            ],
            launch.RepoPath);

        return new ReviewerInvocation(vendor, ConsultantRoles.Consult, request, string.Empty, inner, Model: launch.Settings.Model);
    }

    /// <summary>
    /// A question row: the base, the plan's flags — <c>--tools</c> / <c>--restricted</c> / <c>--add-dir</c>,
    /// never <c>--disallowedTools</c> (F3, F5) — and nothing of this adapter's own; one shot, in the
    /// plan's directory (PLAN_question_consultant.md, D4).
    /// </summary>
    private ReviewerInvocation Planned(ConsultantLaunch launch, Confinement.Planned plan)
    {
        var request = Request(launch,
            [
                "-p",
                "--output-format", "json",
                .. plan.Flags,
                NoMcpServers.ClaudeFlag,
                .. Model(launch.Settings),
            ],
            ConsultantLaunches.Cwd(launch, plan));

        return new ReviewerInvocation(
            vendor, ConsultantRoles.Question, ConsultantLaunches.ForQuestion(request), string.Empty, inner, Model: launch.Settings.Model);
    }

    private ProcessRequest Request(ConsultantLaunch launch, string[] arguments, string cwd)
    {
        var request = new ProcessRequest(
            Executable(launch.Settings),
            arguments,
            cwd)
        {
            Environment = launch.Settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = launch.Settings.ApiKey }
                : [],
            StdIn = launch.Prompt,
            Timeout = launch.Settings.Timeout,
        };
        ConsultantLaunches.MustCarryNoLineBreak(request.Arguments);

        return request;
    }

    /// <summary>The model, then the row's fast mode in the reviewer's own spelling (todo/PLAN_fast_mode.md).</summary>
    private static IEnumerable<string> Model(ReviewerSettings settings) =>
        [.. settings.Model.Length > 0 ? (string[])["--model", settings.Model] : [], .. ClaudeFastMode.Args(settings)];

    /// <summary>
    /// <c>--restricted</c> for a CLI that declared it, nothing for one whose help came back without it — and a
    /// contract violation for anything else.
    /// </summary>
    /// <remarks>
    /// <see cref="ReviewerSettings.ClaudeCli"/> defaults to <see cref="ClaudeCapability.Unprobed"/>, so a call site that
    /// builds a claude consultant without <see cref="PrepareAsync"/> throws here — in its first test — rather than
    /// shipping an argv without the flag for a claude that has it. <see cref="RestrictedSupport.Unknown"/> never
    /// reaches here from <c>PrepareAsync</c>, which refuses the turn on it; arriving with it is the same violation.
    /// </remarks>
    private static string[] RestrictedArgument(ClaudeCapability cli) => cli.Support switch
    {
        RestrictedSupport.Declared => [ClaudeCapability.RestrictedFlag],
        RestrictedSupport.NotDeclared => [],
        _ => throw new ArgumentException(
            $"a claude consultant launch must be prepared before it is built (IConsultantRuntime.PrepareAsync) — its capability is {cli.Support}: {cli.Reason}",
            nameof(cli)),
    };

    /// <summary>The CLI the probe asks and the turn launches — one road, so the two cannot be different programs.</summary>
    private string Executable(ReviewerSettings settings) => CapabilityExecutable(settings.ExecutablePath);

    /// <summary>
    /// The executable whose <c>--help</c> decides the argv — the one the turn launches, which <c>--consultants</c> asks
    /// too, so the survey and the turn cannot be asking two programs (the whole-branch review removed the survey's copy).
    /// </summary>
    public string CapabilityExecutable(string configuredExecutable) =>
        configuredExecutable.Length > 0 ? configuredExecutable : "claude";

    /// <summary>
    /// A silent claude launch whose envelope records a refused READ tool — <c>Read</c>, <c>Glob</c> or <c>Grep</c>, the only
    /// tools it is offered — is <c>read-denied</c>, naming the tool; anything else is the empty answer as it was.
    /// </summary>
    /// <remarks>
    /// No <c>command-denied</c> here: claude is offered no shell to be refused, and that case's sentence ("it did not
    /// answer even when told the command would not come") describes agy's follow-up, which claude has none of.
    /// </remarks>
    public ConsultFailure SilentFailure(ReviewerLaunch launched, ConsultFailure unexplained) =>
        Denials(launched).FirstOrDefault(denial => ReadToolNames.Contains(denial.Action, StringComparer.Ordinal)) is { } read
            ? new ConsultFailure.ReadDenied(read.Action)
            : unexplained;

    private static readonly string[] ReadToolNames = [.. ConfinementPlanner.ClaudeDiskTools];

    /// <summary>The <c>session_id</c> of the envelope, which the CLI reports whether or not it was given one.</summary>
    public string ReadHandle(ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.StdOut);

            return document.RootElement.TryGetProperty("session_id", out var id)
                   && id.ValueKind == JsonValueKind.String
                   && ConsultantHandle.IsWellFormed(id.GetString())
                ? id.GetString()!
                : string.Empty;
        }
        catch (JsonException)
        {
            // A killed turn leaves a truncated envelope, which is not an error here — it is simply
            // a turn whose handle nobody can read, and the record keeps whatever it already had.
            return string.Empty;
        }
    }

    /// <summary>The tools the CLI refused this launch — the <c>tool_name</c> of each <c>permission_denials</c> entry of its envelope.</summary>
    /// <remarks>
    /// <para>Epic 4 of PLAN_the_consultant_works_on_every_vendor.md: the consultant check reports its canary as
    /// <c>denied-by-cli</c> only on the CLI's own record of a refusal (cadence consultation 435b1b25 — a model that
    /// declines on its own proves compliance, not confinement), and the antigravity adapter already read its stream's
    /// <c>denied_actions</c>. Read from the envelope's top-level field and nowhere else: the answer is the model
    /// speaking, and a model quoting the field name has refused nothing. A torn envelope — a killed turn — is no
    /// record at all.</para>
    /// <para>The words are claude's tool names (<c>Read</c>, <c>Glob</c>), kept as they are. None of them is a word
    /// <c>ConsultFailures</c> classifies an empty answer by (<c>command</c>, <c>read_file</c>, <c>read_url</c>), so an
    /// empty claude turn still classifies as it did before this existed.</para>
    /// </remarks>
    public IReadOnlyList<string> DeniedActions(ReviewerLaunch launched) => [.. Denials(launched).Select(denial => denial.Action)];

    /// <summary>
    /// <see cref="DeniedActions"/> with what each refused tool was asked for — the <c>file_path</c>, <c>path</c> or
    /// <c>pattern</c> of its <c>tool_input</c>, empty when it named none — so the consultant check counts a refusal as the
    /// canary's only when it names the canary (epic 4's code round).
    /// </summary>
    public IReadOnlyList<DeniedAction> Denials(ReviewerLaunch launched)
    {
        if (launched.Process is not { } process)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(process.StdOut);

            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("permission_denials", out var denials)
                   && denials.ValueKind == JsonValueKind.Array
                ? [.. denials.EnumerateArray().Select(DenialOf).Where(denial => denial.Action.Length > 0)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly string[] TargetFields = ["file_path", "path", "pattern"];

    private static DeniedAction DenialOf(JsonElement denial) =>
        denial.ValueKind == JsonValueKind.Object
            ? new DeniedAction(StringIn(denial, "tool_name"), TargetOf(denial))
            : new DeniedAction(string.Empty, string.Empty);

    /// <summary>The first of the input fields a read tool names its target by — empty when the denial carries none.</summary>
    private static string TargetOf(JsonElement denial) =>
        denial.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object
            ? TargetFields.Select(field => StringIn(input, field)).FirstOrDefault(value => value.Length > 0) ?? string.Empty
            : string.Empty;

    private static string StringIn(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// The CLI's own refusal to resume — read from STDERR and a failed exit only.
    /// </summary>
    /// <remarks>
    /// Never from stdout, which carries the model's ANSWER: a consultant advising about an error
    /// message would have its perfectly good turn discarded and its handle reset for quoting the
    /// words "conversation not found". Stderr plus a non-zero exit is the CLI speaking; stdout is the
    /// model speaking. (codex, code round.)
    /// </remarks>
    public bool DroppedTheConversation(ProcessResult result) =>
        result.ExitCode != 0
        && (result.StdErr.Contains("No conversation found", StringComparison.OrdinalIgnoreCase)
            || result.StdErr.Contains("session not found", StringComparison.OrdinalIgnoreCase)
            || result.StdErr.Contains("No session found", StringComparison.OrdinalIgnoreCase));
}
