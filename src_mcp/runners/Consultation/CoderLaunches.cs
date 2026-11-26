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

    /// <summary>The sentence a runtime that has no coder type is refused with: a write launch is built only by a vendor that has an <see cref="ICoderVendor"/>, and is launched only when the table ships it.</summary>
    public const string NoCoderLaunch =
        "this runtime has no coder launch: a write launch is built only by a vendor that has a coder type, and launched only on a vendor that shared/coder-boundary-releases.json ships";

    /// <summary>What a person can do about <see cref="NoCoderLaunch"/>.</summary>
    public const string NoCoderLaunchCure = "set the coder's model to a model of a launchable coder vendor (claude, or a vendor whose row of the table of coder boundaries has the verdict ship and the mark launchable on) in ConnectOtherAIs";

    /// <summary>The refusal every runtime but claude answers a coder launch with, before anything is launched.</summary>
    public static ConsultantPreparation.Refused NoCoder { get; } = new(new ConsultFailure.VendorRefused(NoCoderLaunch, NoCoderLaunchCure));

    private static readonly StringComparer SamePlace = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Why a coder launch must not start — empty when its job, worktree and handle agree (and, when <paramref name="vendors"/> is given, its vendor ships). The vendor comes first (epoch 3 E3 s7 t1): a vendor the table does not ship is refused with a sentence that names the shipped ones,
    /// before anything else is asked of the launch. The launcher (<c>RoleModelLauncher</c>) asks with its own set, so the adapter that builds the argv asks only about the job, the worktree and the handle.
    /// </summary>
    internal static string WhyNot(LaunchConfinement.Coder coder, CoderVendorSet? vendors = null) =>
        vendors?.Resolve(coder.Vendor) is CoderVendorResolution.Refused refused ? refused.Sentence
        : WhyNotHere(coder) is { Length: > 0 } here ? here : WhyNotTheHandle(coder);

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

        if (!string.Equals(handle.Vendor, coder.Vendor, StringComparison.Ordinal))
        {
            return $"the coder's resume handle belongs to the {handle.Vendor} CLI and this launch is for {coder.Vendor}, and a conversation is never continued on another vendor's CLI — resume a job only on the vendor that started it";
        }

        if (handle.JobId != coder.JobId)
        {
            return "the coder's resume handle belongs to another job, and a conversation is never continued by a job that did not start it — resume a job only with its own handle";
        }

        if (!SamePlace.Equals(handle.Worktree, Normalised(coder.Worktree)))
        {
            return "the coder's resume handle belongs to another worktree, and a conversation is never continued in a folder it did not start in — resume a job only in its own worktree";
        }

        return string.Equals(handle.KeyId, coder.KeyId, StringComparison.Ordinal)
            ? string.Empty
            : "the coder's resume handle belongs to another claude key, and a session is resumed only with the key that started it — resume a job only with the key of its session";
    }

    /// <summary>A worktree path as a handle remembers it: full, with no trailing separator.</summary>
    public static string Normalised(string worktree) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktree));

    /// <summary>The request of a coder launch as every caller hands it on: the minimal child environment, applied here and nowhere else.</summary>
    public static ProcessRequest ForCoder(ProcessRequest request) => ConsultantLaunches.WithMinimalEnvironment(request);
}
