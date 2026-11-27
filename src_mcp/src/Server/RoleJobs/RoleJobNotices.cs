using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The notices a role job writes about itself (spec A): <c>role-job-stalled</c> — one per stall — <c>role-job-lost</c> — one per job — <c>role-job-failed</c> — one per failed job — and <c>role-model-switched</c> — one per
/// switch of model (subject <c>&lt;jobId&gt;/&lt;n&gt;</c>, and it NAMES THE VENDOR the work goes to: the repository text goes with it). The subject is the job id, so a repeat of the same
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

    /// <summary>One model switch: the job's <paramref name="attempt"/>-th attempt stopped (<paramref name="stop"/>) and the work goes on <paramref name="to"/>. Names both vendors; the Provider field is the one the text goes to.</summary>
    public static ServerNotice Switched(RoleJobRecord job, ModelRef from, ModelRef to, RoleStop stop, int attempt, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "outcome",
        Source = Source,
        Code = ServerNoticeCodes.RoleModelSwitched,
        Subject = $"{job.JobId}/{attempt}",
        Title = $"Role job {job.JobId} ({job.Role}) moved from {RoleRoute.Format(from)} to {RoleRoute.Format(to)} after {stop.Word}",
        Detail = $"{RoleRoute.Format(from)} stopped ({stop.Word}): {stop.Reason} The job now sends its repository text to the {to.Vendor} vendor ({to.Model}).{(from.Vendor == to.Vendor ? string.Empty : $" That is another vendor than {from.Vendor}.")}",
        Cure = "Nothing is needed. To keep the work with one vendor, change the role's list in COAI_ROLES_MODELS.",
        Role = job.Role,
        Repo = job.Repo,
        Provider = to.Vendor,
    };

    /// <summary>
    /// The instruction step of a coder job printed text that looks like a credential: the job went on, and this says what was found — the CLASS and the LINE of each finding, never a part of the value. The subject is the job id, so one job's findings coalesce.
    /// </summary>
    public static ServerNotice InstructionsSecret(RoleJobRecord job, IReadOnlyList<InstructionSecret> findings, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.InstructionsSecret,
        Subject = job.JobId,
        Title = $"Role job {job.JobId} ({job.Role}): the instruction step printed text the redactor flags ({findings.Count} finding{(findings.Count == 1 ? string.Empty : "s")})",
        Detail = $"Found: {string.Join("; ", findings.Select(finding => finding.Line > 0 ? $"{finding.Class} at line {finding.Line}" : $"{finding.Class} in the output as a whole"))}. The job went on and the output reached the prompt of every model as it was; only the class and the line are written down, never the value.",
        Cure = "Change the step so that it prints nothing of the kind, and rotate what it printed if that was real. The output is kept in the job folder (instructions/stdout.txt).",
        Role = job.Role,
        Repo = job.Repo,
    };

    /// <summary>The real sign-in file of a vendor differs between the start and the end of a coder job: coai never writes it, so the person is told that something else did. Names the vendor and the two modification times; no text, no hash.</summary>
    public static ServerNotice SignInFileChanged(RoleJobRecord job, CoderSignInRecord record, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "outcome",
        Source = Source,
        Code = ServerNoticeCodes.SignInFileChanged,
        Subject = job.JobId,
        Title = $"Role job {job.JobId} ({job.Role}): the {record.Vendor} sign-in file changed while the job ran",
        Detail = $"The file was changed at {(record.BeforeModifiedUtc.Length > 0 ? record.BeforeModifiedUtc : "(absent)")} before the job and at {(record.AfterModifiedUtc.Length > 0 ? record.AfterModifiedUtc : "(absent)")} after it. The job worked on a copy and never wrote the real file; its envelope holds both hashes.",
        Cure = "Nothing is needed when you used the vendor's own CLI meanwhile. If you did not, look at who wrote the file.",
        Role = job.Role,
        Repo = job.Repo,
    };

    /// <summary>A job that ended <c>failed</c>: its stop and its reason, the way <c>role_status</c> shows them.</summary>
    public static ServerNotice Failed(RoleJobRecord job, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.RoleJobFailed,
        Subject = job.JobId,
        Title = $"Role job {job.JobId} ({job.Role}) failed: {job.Stop}",
        Detail = job.Reason,
        Cure = "role_status names every attempt and the stop of each. Fix what the stop says (a limit, a sign-in, a model name) and start the role again.",
        Role = job.Role,
        Repo = job.Repo,
    };

    /// <summary>A failed test passed on the one re-run and was excused (E4 s5 t5): the checks counted it as passed. ONE per job. The names are the tests' own and never a value of the output.</summary>
    public static ServerNotice TestFlaky(RoleJobRecord job, int count, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "outcome",
        Source = Source,
        Code = ServerNoticeCodes.CoderTestFlaky,
        Subject = job.JobId,
        Title = $"Role job {job.JobId} ({job.Role}): {count.ToString(System.Globalization.CultureInfo.InvariantCulture)} failed test(s) passed on a re-run and were counted as passed",
        Detail = "The result lists each under 'flaky' with its proof (the test ran again and passed, in a re-run that itself exited 0). A test that fails once and passes once may be an order dependency of the job's change: it is listed, never hidden.",
        Cure = "Look at the 'flaky' list of the result; a test that is flaky in the base is a problem of the repository, not of this job.",
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
