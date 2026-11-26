using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The resume handle of a coder turn: the CLI's session id, BOUND to the job and the worktree it was made for (epoch 3 E2 s2 t3).
/// </summary>
/// <remarks>
/// <para>Measured 2026-11-23 (<c>v2/spikes/RESULTS_coder_write_boundary.md</c>, item M7): claude 2.1.330 resumed a session id from a
/// DIFFERENT working folder, so the CLI itself ties a session to nothing. A session id alone would let the turn of one job continue in the
/// worktree of another, with that job's conversation behind it. So the id travels as this value, made only by <see cref="Bind"/> from the job
/// and the worktree of the turn that produced it, and a launch compares all three before a process starts
/// (<see cref="CoderLaunches.WhyNot"/>).</para>
/// <para>The worktree is compared as a full path, spelled the way the launch spelled it: a second spelling of the same folder (a link) is refused,
/// which is the safe side of "cannot tell".</para>
/// <para><b>The vendor is part of the binding</b> (epoch 3 E3 s7 t1, V5): a codex thread id is not a claude session id, and a handle made for one vendor continues nothing of another's, so a launch whose vendor is not the handle's is refused before a process starts.</para>
/// <para><b>The key is part of the binding</b> (epoch 3 E2 s7): a role job on the claude key pool resumes a session with the key that started it, so the handle carries the key id (empty for a launch with no key) and a launch whose key is another is refused.</para>
/// </remarks>
public sealed record CoderHandle(string JobId, string Worktree, string SessionId, string KeyId = "", string Vendor = CoderBoundaryTable.Claude)
{
    /// <summary>No session yet: the first turn of a job.</summary>
    public static CoderHandle None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public bool IsNone => SessionId.Length == 0;

    /// <summary>
    /// The handle of the turn that ran for <paramref name="coder"/> and reported <paramref name="sessionId"/> — or <see cref="None"/> when the id is not one the CLI could have said
    /// (it is untrusted by position, as <see cref="ConsultantHandle"/> says) or the turn's own job and worktree were not usable.
    /// </summary>
    public static CoderHandle Bind(LaunchConfinement.Coder coder, string sessionId) =>
        ConsultantHandle.IsWellFormed(sessionId) && CoderLaunches.WhyNotHere(coder).Length == 0
            ? new CoderHandle(coder.JobId, CoderLaunches.Normalised(coder.Worktree), sessionId, coder.KeyId, coder.Vendor)
            : None;
}
