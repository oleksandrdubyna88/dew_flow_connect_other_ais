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
    /// <see cref="Stop"/> asked BEFORE the launch's own stop is read, where no earlier stop (the layer's, a refusal, a key) says more: a denied action leaves an empty answer, and read first as "no answer" it would be a crash that fails over instead of the boundary fault it is.
    /// </summary>
    public static RoleStop? StopFirst(CoderVendorSet vendors, ModelRef model, RoleLaunched launched, string expectedSession, Runners.Toolchain.BoundaryJob? job) =>
        launched.OsStop is null && launched.Refused is null && launched.KeyStop is null && !launched.KeyExposed && launched.ExposedIds.Count == 0 && launched.Launch?.Process is { ExitCode: 0, TimedOut: false, Cancelled: false }
            ? Stop(vendors, model, launched, expectedSession, job)
            : null;

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
