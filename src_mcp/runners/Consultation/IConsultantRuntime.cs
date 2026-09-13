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
    string AnswerSchemaFile = "");

/// <summary>The role every consultation launch carries — a string, since roles became data.</summary>
public static class ConsultantRoles
{
    public const string Consult = "consult";
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

    private static readonly string[] Extensions = [".txt", ".prompt", ".json"];

    /// <summary>A file name this product wrote for a consultation.</summary>
    public static bool Ours(string fileName) =>
        fileName.Contains(Marker, StringComparison.Ordinal)
        && Array.Exists(Extensions, e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>A fresh artefact name for one launch. Unique, so two launches cannot share a file.</summary>
    public static string Name(string vendor, string extension) =>
        $"{Core.Rounds.FileName.Safe(vendor)}{Marker}{Guid.NewGuid():N}{extension}";
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
public interface IConsultantRuntime
{
    string Vendor { get; }

    ConsultantMemory Memory { get; }

    /// <summary>The process for ONE turn. Throws on a malformed handle — the caller validates first.</summary>
    ReviewerInvocation Build(ConsultantLaunch launch);

    /// <summary>The vendor's own id for this conversation, read off whatever the process said — or empty.</summary>
    string ReadHandle(ProcessResult result);

    /// <summary>
    /// This route cannot run without the answer schema on disk, so a failure to provision it is a
    /// refusal for this route alone.
    /// </summary>
    /// <remarks>
    /// False for the three CLIs, which answer prose and are handed no schema at all — a read-only
    /// data directory must not stop a consultation that never needed the file.
    /// </remarks>
    bool NeedsAnswerSchema => false;

    /// <summary>
    /// The advice out of whatever shape this vendor answers in. Prose for every CLI; the one
    /// schema-bound route unwraps its envelope.
    /// </summary>
    /// <remarks>
    /// On the ADAPTER rather than in the service, because it is vendor knowledge: unwrapping every
    /// answer unconditionally mangled a CLI's prose that happened to be JSON with an <c>answer</c>
    /// property — a consultant asked about a configuration file could return one — and a future route
    /// with its own envelope would have meant another branch in the service. (gemini, story 2's code
    /// round.) An empty string is returned AS an empty string, so the service's own
    /// "the consultant answered nothing" path fires instead of the envelope being shown as advice.
    /// </remarks>
    string ReadAdvice(string raw) => raw;

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
}
