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
/// <para>Two things it inherits from the reviewer path rather than re-deciding. The shim takes the
/// cross-process engine lease, so one card serves one caller at a time however many rounds and
/// consultations are in flight. And it refuses a request with no schema — deliberately, because an
/// unconstrained local request is answered with an invented shape after a full generation has been
/// paid for — which is what <see cref="ConsultAnswerSchema"/> exists for.</para>
/// </remarks>
public sealed class LocalConsultant(LocalRuntime inner, string vendor, string dataDir) : IConsultantRuntime
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
            // NOT under `consultations/`: that directory holds one file per consultation, keyed by
            // id, and story 2's live check found this schema being read back as a record with no id
            // at all. A directory with a shape belongs to that shape alone.
            ConsultAnswerSchema.EnsureFile(Path.Combine(dataDir, "schemas")),
            launch.OutputDir,
            launch.Settings);
        ConsultantLaunches.MustCarryNoLineBreak(built.Request.Arguments);

        return built;
    }

    /// <summary>Nothing to read: a completion is not a conversation, and there is no id to keep.</summary>
    public string ReadHandle(ProcessResult result) => string.Empty;

    /// <summary>It never held one, so it can never have dropped one.</summary>
    public bool DroppedTheConversation(ProcessResult result) => false;
}

/// <summary>
/// The consultant's answer out of a schema-bound reply, or the raw text when it is already prose.
/// </summary>
/// <remarks>
/// The three CLIs answer prose and this passes it straight through; the local engine answers
/// <c>{"answer": "…"}</c> because its route refuses to run without a schema. One reader rather than a
/// branch in the service, so "what did the consultant actually say" has a single answer.
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

            return document.RootElement.TryGetProperty("answer", out var answer)
                   && answer.ValueKind == JsonValueKind.String
                   && answer.GetString() is { Length: > 0 } text
                ? text
                : raw;
        }
        catch (JsonException)
        {
            // Prose that happens to begin with a brace is still the answer.
            return raw;
        }
    }
}
