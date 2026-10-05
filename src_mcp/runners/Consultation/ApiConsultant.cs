using CoaiMcp.Core.Feature;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// A hosted OpenAI-compatible API as a QUESTION consultant (PLAN_question_consultant.md, S1): one
/// completion through <c>coai-mcp --ask-api</c>, on a <c>none</c> grant only, given the question, the
/// checked context and the outline of what exists — and, when asked, source from the committed HEAD.
/// </summary>
/// <remarks>
/// <para><b>Composed from <see cref="ApiRuntime"/> the way <c>LocalConsultant</c> composes
/// <c>LocalRuntime</c></b>: the reviewer adapter builds the whole shim invocation — the endpoint, the
/// dialect, the deadline, the token ceiling, the key in the child's ENVIRONMENT and nowhere else — and
/// this changes only the role, the directory it stands in, the schema it is bound to and the
/// environment it inherits. The vault key is read by the caller under <see cref="VaultKeyName"/> (the
/// row's key name, S3.6) into <c>ReviewerSettings.ApiKey</c>; this class never sees the vault.</para>
/// <para><b>One shot, <c>none</c> only.</b> A hosted completion has no process and no tools (F8), so a
/// <c>disk</c> or <c>web</c> plan is a contract violation here — the matrix refuses the pair first, and
/// this refuses it again by name rather than launching something that would do nothing. The stuck
/// consultant's shape (<c>LaunchConfinement.AsShipped</c>) is refused too: an api row was never a
/// consultant (<c>ConsultantResolution.Consulting</c> is unchanged).</para>
/// <para><b>The source turns (A9).</b> The model asks in <c>sourceRequests</c>; <see cref="AfterAsync"/>
/// serves the requests through the round's <see cref="SourceResolver"/> — git objects at the pinned
/// HEAD, redacted, capped — and hands back the SAME planned launch with the base prompt plus a tail
/// (D25). The limit, stated in the prompt: the resolver reads COMMITTED code; uncommitted work reaches
/// an api row only through the context.</para>
/// </remarks>
public sealed class ApiConsultant(IReviewerRuntime inner, string vendor, string vaultKeyName, QuestionMaterial? material = null)
    : IConsultantRuntime, IAnsweringFollowUps
{
    /// <summary>
    /// How much earlier conversation travels into each stuck-consultation turn (PLAN_one_model_catalog.md E2.3). Twice the
    /// local route's: a hosted model's context window is far larger than a local card's, and the prompt already carries
    /// the working-tree diff at up to 64 KB. The record freezes whatever this build declared when the consultation opened.
    /// </summary>
    public const int CarryBudget = 32 * 1024;

    /// <summary>A hosted completion keeps no conversation — the consultation record carries it, as for a local engine.</summary>
    public ConsultantMemory Memory => new ConsultantMemory.WeRemember(CarryBudget);

    /// <summary>Nothing to read: a completion is not a conversation, and there is no id to keep.</summary>
    public string ReadHandle(CoaiMcp.Runners.Processes.ProcessResult result) => string.Empty;

    /// <summary>It never held one, so it can never have dropped one.</summary>
    public bool DroppedTheConversation(CoaiMcp.Runners.Processes.ProcessResult result) => false;

    private readonly QuestionMaterial _material = material ?? QuestionMaterial.None;

    public string Vendor => vendor;

    /// <summary>The vault entry the row's key is filed under — its key name, else its id (S3.6).</summary>
    public string VaultKeyName => vaultKeyName;

    /// <summary>The shim refuses a request without a schema; this route is bound to <see cref="QuestionAnswerSchema"/>.</summary>
    public bool NeedsAnswerSchema => true;

    public QuestionMaterial Material => _material;

    public int FollowUps => _material.Source is SourceTurns.On on ? on.FollowUps : 0;

    /// <summary>The same row with the material a question gives it — built per question, never per row.</summary>
    public ApiConsultant With(QuestionMaterial withMaterial) => new(inner, vendor, vaultKeyName, withMaterial);

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        return launch.Confinement is LaunchConfinement.Planned ? Question(launch) : Stuck(launch);
    }

    /// <summary>
    /// A stuck consultation (D9): the reviewer adapter builds the whole shim invocation — the endpoint, the module, the
    /// deadlines, the prompt file — bound to the consultation's answer schema, as the local consultant's is.
    /// </summary>
    private ReviewerInvocation Stuck(ConsultantLaunch launch)
    {
        var built = inner.Build(ConsultantLaunches.RoleOf(launch), launch.Prompt, launch.RepoPath, launch.AnswerSchemaFile, launch.OutputDir, launch.Settings);
        ConsultantLaunches.MustCarryNoLineBreak(built.Request.Arguments);

        return built;
    }

    private ReviewerInvocation Question(ConsultantLaunch launch)
    {
        var plan = QuestionRowOf(launch);
        var built = inner.Build(
            ConsultantRoles.Question,
            launch.Prompt,
            ConsultantLaunches.Cwd(launch, plan),
            launch.AnswerSchemaFile,
            launch.OutputDir,
            launch.Settings);
        ConsultantLaunches.MustCarryNoLineBreak(built.Request.Arguments);

        return built with { Request = ConsultantLaunches.ForQuestion(built.Request) };
    }

    /// <summary>The plan a launch carries, when it is a <c>none</c> question row — anything else is refused by name.</summary>
    private static Confinement.Planned QuestionRowOf(ConsultantLaunch launch) => launch.Confinement switch
    {
        LaunchConfinement.Planned { Plan.Grant.Capability: Capability.None } planned => planned.Plan,
        LaunchConfinement.Planned planned => throw new ArgumentException(
            $"the api runtime cannot do '{planned.Plan.Grant.Capability.Spelled()}': a hosted completion has no process and no tools — only a 'none' prompt runs on 'api'",
            nameof(launch)),
        _ => throw new ArgumentException(
            "the api runtime answers question rows only — it has no stuck-consultant shape to ship, and ConsultantResolution refuses it by name",
            nameof(launch)),
    };

    /// <summary>The envelope's <c>answer</c>, or the prose as it arrived — through the one answer reader.</summary>
    public string ReadAdvice(string raw) => QuestionAnswer.Parse(raw).Answer;

    public async Task<AnsweringTurn> AfterAsync(ConsultantLaunch launch, AnsweringMemory memory, string raw, CancellationToken ct)
    {
        var answer = QuestionAnswer.Parse(raw);
        if (!answer.AsksForSource)
        {
            return new AnsweringTurn.Done(answer.Answer);
        }

        if (WhyNotServed(memory, answer) is { Length: > 0 } why)
        {
            return new AnsweringTurn.Done(answer.Answer, why);
        }

        // WhyNotServed answers empty only when the source turns are on.
        var on = (SourceTurns.On)_material.Source;
        var served = await on.Resolver.ServeAsync(answer.Requests, memory.Spent, ct);
        var next = memory.Turn + 1;
        var final = next >= Turns(on) || served.Exhausted;
        var tail = QuestionTail.Render(new QuestionTailInput(
            next, Turns(on), answer.Requests, answer.RejectedRequests, memory.ServedSoFar, served, final));

        return new AnsweringTurn.Next(
            launch with { Prompt = memory.Base + tail },
            memory with { Turn = next, Spent = served.Spent, ServedSoFar = memory.ServedSoFar.AddRange(served.Served), SaidFinal = final },
            $"turn {next}: {(served.IsEmpty ? "nothing to serve" : served.Summary())}");
    }

    /// <summary>Why a request will not be served — the stops, in the order they are asked — or empty when the conversation goes on.</summary>
    private string WhyNotServed(AnsweringMemory memory, QuestionAnswer answer) =>
        _material.Source is not SourceTurns.On on ? NotServed(answer, "follow-ups are off on this row")
        : memory.SaidFinal ? NotServed(answer, "the last turn was final")
        : memory.Turn >= Turns(on) ? NotServed(answer, $"the follow-up cap ({on.FollowUps}) was reached")
        : string.Empty;

    /// <summary>A request no turn will answer is written down, never dropped — the feature review's own rule.</summary>
    private static string NotServed(QuestionAnswer answer, string why) =>
        $"{why}; asked for source that was not served: "
        + string.Join("; ", [
            .. answer.Requests.Select(SourceRequestNote.Line),
            .. answer.RejectedRequests.Select(r => $"request {r.Index} could not be read: {r.Reason}")]);

    private static int Turns(SourceTurns.On on) => 1 + on.FollowUps;
}
