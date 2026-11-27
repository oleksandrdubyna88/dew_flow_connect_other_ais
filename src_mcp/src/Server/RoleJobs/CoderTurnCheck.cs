using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The check of a finished coder turn's OWN stream, as the vendor's seam gives it (epoch 3 E3 s7 t2, judgement S7): asked after EVERY turn — turn 0, the repair turn and every fix turn — BEFORE anything of that turn is committed. One road for
/// <see cref="RoleFailover"/> (turn 0 and its repair) and <see cref="CoderFixLoop"/> (fix turns), so a vendor's check cannot be asked in one and forgotten in the other.
/// </summary>
internal static class CoderTurnCheck
{
    /// <summary>The kind a stop of this check is filed under.</summary>
    public const string BoundaryKind = "turn-check";

    public const string SessionKind = "session";

    /// <summary>
    /// <see cref="Stop"/> asked BEFORE the launch's own stop is read, for EVERY launch that has a process, a cut stream too (a deadline, a non-zero exit, a cancel): where no earlier stop (the layer's, a refusal, a key) says more. A denied action leaves an empty answer, and a model that keeps a turn going to
    /// its deadline would otherwise hide a boundary that broke behind a crash that fails over. A vendor answers a cut stream with a boundary only for what one side alone proves (pass 5, M11); a boundary wins over a crash and a limit.
    /// </summary>
    public static RoleStop? StopFirst(CoderVendorSet vendors, ModelRef model, RoleLaunched launched, string expectedSession, Runners.Toolchain.BoundaryJob? job) =>
        launched.OsStop is null && launched.Refused is null && launched.KeyStop is null && !launched.KeyExposed && launched.ExposedIds.Count == 0
            ? Stop(vendors, model, launched, expectedSession, job)
            : null;

    /// <summary>How long a check of a turn that the job's own token cut may take: the check is a few reads of files, and it is never given the cancelled token (a check that is cancelled checks nothing).</summary>
    public static readonly TimeSpan CutCheckBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <see cref="StopFirst"/> for a turn that the JOB's token cut (its budget ran out, or the person stopped it), with a short time of its own (pass 5 re-check, M11 leg a): such a turn is checked like any other, and only a boundary comes of it — the job's own word (<c>timeout</c>, <c>stopped</c>) stands for everything else.
    /// A boundary outranks it, because a model that keeps a turn going until the budget would otherwise hide what broke. Null when nothing is proved, or when the check did not finish in its time.
    /// </summary>
    public static async Task<RoleStop?> StopOfACutTurnAsync(CoderVendorSet vendors, ModelRef model, RoleLaunched launched, string expectedSession, Runners.Toolchain.BoundaryJob? job)
    {
        try
        {
            var stop = await Task.Run(() => StopFirst(vendors, model, launched, expectedSession, job), CancellationToken.None).WaitAsync(CutCheckBudget, CancellationToken.None);

            return stop is { Word: RoleStopWords.CoderBoundary } ? stop : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// The stop a finished turn came to by its own stream, or null when the stream says nothing wrong. A wrong answer ends the job <c>coder-boundary</c> (never green, never a failover: the next model would meet the same layer); a session that is not the
    /// handle's, or is lost, is a <c>crash</c>, so the next model starts from the last coai commit.
    /// </summary>
    /// <param name="vendors">The vendors a coder can be launched on.</param>
    /// <param name="model">The model of the turn.</param>
    /// <param name="launched">What the launch came to; one with no process (a refusal before the launch) has no stream and is not asked.</param>
    /// <param name="expectedSession">The session of the job's handle, or empty on turn 0.</param>
    public static RoleStop? Stop(CoderVendorSet vendors, ModelRef model, RoleLaunched launched, string expectedSession, Runners.Toolchain.BoundaryJob? job = null)
    {
        if (launched.Launch?.Process is not { } process || vendors.Resolve(model.Vendor) is not CoderVendorResolution.Launchable found)
        {
            return null;
        }

        return (job is null ? found.Vendor.CheckTurn(process, expectedSession) : found.Vendor.CheckTurn(process, expectedSession, job)) switch
        {
            CoderTurnVerdict.Boundary wrong => new RoleStop(RoleStopWords.CoderBoundary, Redaction.SafeText(wrong.Sentence, Redaction.TitleLimit), BoundaryKind, false, false),
            CoderTurnVerdict.Crash lost => new RoleStop(RoleStopWords.Crash, Redaction.SafeText(lost.Sentence, Redaction.TitleLimit), SessionKind, false, false),
            _ => null,
        };
    }
}
