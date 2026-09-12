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
