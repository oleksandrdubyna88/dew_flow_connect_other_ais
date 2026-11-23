using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The notices a role job writes about itself (spec A): <c>role-job-stalled</c> — one per stall — and <c>role-job-lost</c> — one per job. The subject is the job id, so a repeat of the same
/// code for the same job coalesces on the page; the title names the role and the state and carries no round or error sentence (the key is <c>(code, subject)</c>).
/// </summary>
internal static class RoleJobNotices
{
    private const string Source = "role-jobs";

    public static ServerNotice Stalled(RoleJobRecord job, DateTime nowUtc, TimeSpan silent) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.RoleJobStalled,
        Subject = job.JobId,
        Title = $"Role job {job.JobId} ({job.Role}) has not shown a sign of life for {(int)silent.TotalMinutes} minutes",
        Detail = "Its runner still holds the lease, so it is stopped or hung, not dead; the job is NOT taken over. role_status shows stalled: true; role_cancel stops it.",
        Cure = "Wait if the machine was asleep; otherwise cancel the job with role_cancel and start it again.",
        Role = job.Role,
        Repo = job.Repo,
    };

    public static ServerNotice Lost(RoleJobRecord job, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.RoleJobLost,
        Subject = job.JobId,
        Title = $"Role job {job.JobId} ({job.Role}) was lost: its runner ended without a result",
        Detail = "Nothing held the job's lease any more, so the runner was gone; the job was closed as lost and the CLI child it recorded was stopped. Nothing was published.",
        Cure = "Start the role again.",
        Role = job.Role,
        Repo = job.Repo,
    };
}
