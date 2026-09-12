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
public sealed record ConsultantLaunch(
    string RepoPath,
    string Prompt,
    string Handle,
    string OutputDir,
    ReviewerSettings Settings);

/// <summary>The role every consultation launch carries — a string, since roles became data.</summary>
public static class ConsultantRoles
{
    public const string Consult = "consult";
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
/// </remarks>
public interface IConsultantRuntime
{
    string Vendor { get; }

    ConsultantMemory Memory { get; }

    /// <summary>The process for ONE turn. Throws on a malformed handle — the caller validates first.</summary>
    ReviewerInvocation Build(ConsultantLaunch launch);

    /// <summary>The vendor's own id for this conversation, read off whatever the process said — or empty.</summary>
    string ReadHandle(ProcessResult result);

    /// <summary>The vendor no longer holds this conversation: a resume that cannot be honoured.</summary>
    bool DroppedTheConversation(ProcessResult result);
}
