using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// What the write launch of a coder checks before a process starts, and the one place it says so (epoch 3 E2 s2 t3): the job and the worktree agree, a resumed turn carries
/// its own handle, a vendor other than claude is refused, and the child gets the minimal environment.
/// </summary>
/// <remarks>
/// A sentence here ends with what to do, because a <see cref="ConsultFailure.LaunchRefused"/> is read as one and a <c>ClaudeConsultant.Build</c> contract violation
/// carries the same text.
/// </remarks>
internal static class CoderLaunches
{
    /// <summary>The label a coder turn is filed under.</summary>
    public const string Role = "coder";

    /// <summary>The sentence a vendor other than claude is refused with.</summary>
    public const string ClaudeOnly =
        "the coder is claude only in this epoch: a write launch is measured and built for the claude CLI alone, and no other vendor's CLI is given one";

    /// <summary>What a person can do about <see cref="ClaudeOnly"/>.</summary>
    public const string ClaudeOnlyCure = "set the coder's model to a claude model in ConnectOtherAIs";

    /// <summary>The refusal every runtime but claude answers a coder launch with, before anything is launched.</summary>
    public static ConsultantPreparation.Refused NotClaude { get; } = new(new ConsultFailure.VendorRefused(ClaudeOnly, ClaudeOnlyCure));

    private static readonly StringComparer SamePlace = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Why a coder launch must not start — empty when its job, worktree and handle agree.</summary>
    public static string WhyNot(LaunchConfinement.Coder coder) =>
        WhyNotHere(coder) is { Length: > 0 } here ? here : WhyNotTheHandle(coder);

    /// <summary>Why the job and the worktree of a coder launch are not usable together, whatever its handle — empty when they are.</summary>
    public static string WhyNotHere(LaunchConfinement.Coder coder)
    {
        if (!CoderWorktree.IsJobId(coder.JobId))
        {
            return "the coder job id is not usable: it must be 1 to 40 characters of a-z, 0-9 and '-', starting with a letter or a digit — start the coder with the id coai gave the job";
        }

        if (!Path.IsPathFullyQualified(coder.Worktree))
        {
            return "the coder worktree is not an absolute path — start the coder in the folder coai made for the job";
        }

        return Path.GetFileName(Normalised(coder.Worktree)) == CoderWorktree.FolderOf(coder.JobId)
            ? string.Empty
            : $"the folder {Normalised(coder.Worktree)} is not the worktree of job {coder.JobId} (that is {CoderWorktree.FolderOf(coder.JobId)}) — start the coder in the folder coai made for this job";
    }

    /// <summary>A handle is the job's own, for this worktree, with an id the CLI could have said — checked again here, because a value not made by <see cref="CoderHandle.Bind"/> is nobody's word.</summary>
    private static string WhyNotTheHandle(LaunchConfinement.Coder coder)
    {
        var handle = coder.Resume;
        if (handle.IsNone)
        {
            return string.Empty;
        }

        if (!ConsultantHandle.IsWellFormed(handle.SessionId))
        {
            return "the coder's resume handle carries a session id that is not well formed — start this job's turn again without a handle";
        }

        if (handle.JobId != coder.JobId)
        {
            return "the coder's resume handle belongs to another job, and a conversation is never continued by a job that did not start it — resume a job only with its own handle";
        }

        return SamePlace.Equals(handle.Worktree, Normalised(coder.Worktree))
            ? string.Empty
            : "the coder's resume handle belongs to another worktree, and a conversation is never continued in a folder it did not start in — resume a job only in its own worktree";
    }

    /// <summary>A worktree path as a handle remembers it: full, with no trailing separator.</summary>
    public static string Normalised(string worktree) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktree));

    /// <summary>The request of a coder launch as every caller hands it on: the minimal child environment, applied here and nowhere else.</summary>
    public static ProcessRequest ForCoder(ProcessRequest request) => ConsultantLaunches.WithMinimalEnvironment(request);
}
