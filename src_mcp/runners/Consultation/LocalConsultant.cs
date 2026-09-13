using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// A model on this machine as a consultant — the one that keeps NO conversation, so we carry it.
/// </summary>
/// <remarks>
/// <para>It is one HTTP completion per turn through the <c>--ask-local</c> shim: there is no session
/// to resume and no handle to keep, which is what makes it the first and only
/// <see cref="ConsultantMemory.WeRemember"/> here. The transcript therefore travels in the prompt,
/// bounded by the budget declared below and frozen on the consultation's record.</para>
/// <para>Three things it inherits from the reviewer path rather than re-deciding. The shim takes the
/// cross-process engine lease, so one card serves one caller at a time however many rounds and
/// consultations are in flight. It refuses a request with no schema — deliberately, because an
/// unconstrained local request is answered with an invented shape after a full generation has been
/// paid for — which is what <see cref="ConsultAnswerSchema"/> exists for. And <b>its
/// <see cref="Build"/> WRITES the prompt file</b>, which is how a prompt reaches a shim without
/// crossing a Windows argv.</para>
/// <para><b>So Build is pure on the three CLI routes and not on this one</b>, and saying otherwise
/// was wrong: story 2's second code round caught the claim, and the test that was supposed to hold it
/// had been watching a different directory. <c>LocalRuntime.Build</c> has written that file since the
/// review path shipped, and this story reuses that builder rather than forking it — the write is
/// inherited, not introduced. The trigger to move it to the execution boundary is the first caller
/// that wants to BUILD a local launch without running it: a dry run, a preview, a flag inspection.
/// There is none today, and moving it would change the review path for every local reviewer.</para>
/// </remarks>
public sealed class LocalConsultant(IReviewerRuntime inner, string vendor) : IConsultantRuntime
{
    /// <summary>
    /// How much conversation travels into each turn.
    /// </summary>
    /// <remarks>
    /// Smaller than the extension's 60 000-character pipe budget, because this prompt already carries
    /// the working-tree diff at up to 64 KB and a local context window is the smallest of any route
    /// here. The record freezes whatever this build declared when the consultation opened.
    /// </remarks>
    public const int CarryBudget = 16 * 1024;

    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.WeRemember(CarryBudget);

    /// <summary>The one route whose shim refuses an unconstrained request.</summary>
    public bool NeedsAnswerSchema => true;

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        // The reviewer adapter builds the whole shim invocation — the endpoint, the engine lease key,
        // the two deadlines, the token ceiling — and the only thing a consultation changes is WHICH
        // schema it is bound to. Re-deriving any of that here would be a second copy of a decision
        // that has already been measured twice.
        var built = inner.Build(
            ConsultantRoles.Consult,
            launch.Prompt,
            launch.RepoPath,
            // Already on disk, put there when the service was built. Provisioning it HERE made this
            // method impure, put a filesystem write in the core, and forced the data directory into
            // the vendor factory — three reviewers found those three faces of it on one round.
            launch.AnswerSchemaFile,
            launch.OutputDir,
            launch.Settings);
        ConsultantLaunches.MustCarryNoLineBreak(built.Request.Arguments);

        return built;
    }

    /// <summary>Nothing to read: a completion is not a conversation, and there is no id to keep.</summary>
    public string ReadHandle(ProcessResult result) => string.Empty;

    /// <summary>The <c>answer</c> of the schema envelope this route alone is bound to.</summary>
    public string ReadAdvice(string raw) => ConsultantAnswer.TextOf(raw);

    /// <summary>It never held one, so it can never have dropped one.</summary>
    public bool DroppedTheConversation(ProcessResult result) => false;
}

/// <summary>
/// The advice out of the schema envelope the local route is bound to.
/// </summary>
/// <remarks>
/// Used by that route ALONE, through <c>IConsultantRuntime.ReadAdvice</c>. Applying it to every
/// vendor mangled a CLI's prose that happened to be JSON with an <c>answer</c> property — which is
/// exactly what a consultant asked about a configuration file might return.
/// </remarks>
public static class ConsultantAnswer
{
    public static string TextOf(string raw)
    {
        var trimmed = raw.TrimStart();
        if (!trimmed.StartsWith('{'))
        {
            return raw;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);

            // An EMPTY answer is returned as empty, not as the envelope: the service has a path for
            // "the consultant answered nothing", and showing `{"answer":""}` as advice would take it
            // past that path and put our own JSON in front of the caller.
            return document.RootElement.TryGetProperty("answer", out var answer)
                   && answer.ValueKind == JsonValueKind.String
                ? answer.GetString() ?? string.Empty
                : raw;
        }
        catch (JsonException)
        {
            // Prose that happens to begin with a brace is still the answer.
            return raw;
        }
    }
}
