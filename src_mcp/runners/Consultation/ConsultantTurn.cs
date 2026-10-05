using System.Diagnostics;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>What one consultation turn produced — one launch, or a launch and its follow-up.</summary>
/// <param name="Launches">
/// Every launch the turn made, in order: one, or two. Never empty — <see cref="ConsultantTurn"/> is the only
/// thing that builds one, after its first launch. The SAME shape the caller's failure path collects through
/// <c>landed</c>, so the two read the handle and the usage by one rule (<see cref="ConsultantTurn.SurvivingHandle"/>,
/// <see cref="ConsultantTurn.UsageOf"/>) rather than by two that could disagree.
/// </param>
/// <param name="ChangesBeforeFollowUp">
/// What had already changed in the working tree when a follow-up was about to run — in which case none ran.
/// Empty when the tree was clean, or when no follow-up was ever in question (the tree is not asked then).
/// The caller reports a breach from THESE changes rather than snapshotting again: they are what stopped
/// the turn, and a second snapshot could only disagree with them by losing a change somebody reverted.
/// </param>
/// <param name="SurvivingHandle">
/// The vendor's conversation id: the second launch's, else the first's, else empty. A second launch
/// killed before its stream named the id must not lose the conversation the first one opened — that
/// handle is the only way a resume does not pay for the turn twice.
/// </param>
/// <param name="TurnUsage">Both launches billed once (<see cref="ConsultationUsage.OfTwoLaunches"/>), or the one launch's own report.</param>
public sealed record ConsultantTurnResult(
    IReadOnlyList<ReviewerLaunch> Launches,
    IReadOnlyList<TreeChange> ChangesBeforeFollowUp,
    string SurvivingHandle,
    Usage TurnUsage)
{
    /// <summary>The launch whose answer the turn stands on: the follow-up when there was one, else the first.</summary>
    public ReviewerLaunch Final => Launches[^1];

    /// <summary>A second launch continued the first's conversation.</summary>
    public bool FollowedUp => Launches.Count == 2;

    /// <summary>The tree had changed before a follow-up could run, so none did.</summary>
    public bool Breached => ChangesBeforeFollowUp.Count > 0;
}

/// <summary>
/// One consultation turn: a launch, and — when that launch answered nothing in a way the vendor can
/// cure by being told — ONE follow-up in the same conversation, inside the time the turn has left.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The reviewer path got this cure for issue #504 inside
/// <c>ReviewerExecutor.RunAsync</c>, where it is tangled with a findings parse and a repair a
/// consultation does not have. A consultation called <c>LaunchAsync</c> once, so the 2026-10-02
/// antigravity consultations whose <c>run_command</c> was auto-denied came back empty and the caller was
/// told to sharpen a problem statement that was never the problem
/// (PLAN_the_consultant_works_on_every_vendor.md, E1.3). This is the consultation's own one-or-two, built
/// from the same pieces — <see cref="ReviewerExecutor.LaunchAsync"/> for each launch,
/// <see cref="RetryLadder.Remaining"/> and <see cref="RetryLadder.Lesser"/> for the budget — rather than a
/// second copy of the reviewer's.</para>
/// <para><b>Every condition, before a second launch.</b> The first answered NOTHING (a turn that said
/// something, even beside a denial, is returned as it is — measured twice on 2026-10-02) — decided HERE and
/// nowhere else, so an adapter's <c>FollowUp</c> only says what a follow-up would be; the adapter OFFERS one
/// (a vendor with nothing to continue says null); time REMAINS; the tree is still clean, asked through
/// <c>changesSoFar</c>, because a consultant that already wrote to the live checkout has broken the one
/// promise it runs under and continuing it would hand it a second chance to; and time STILL remains once
/// that snapshot is done. <b>Never a third launch</b> — a follow-up that is denied again ends the turn on
/// what it said.</para>
/// <para><b>The time is measured twice, and the second measurement is the one that counts.</b> The tree
/// check is time the follow-up no longer has. A remainder spent by the snapshot is no remainder: a process
/// handed a zero timeout is started and cancelled in the same breath and reports a timeout describing
/// nothing, which is exactly what <c>ReviewerExecutor.NoRepairToRun</c> refuses for the repair — so the turn
/// ends on its first launch instead.</para>
/// <para><b>The second launch's timeout is the LESSER of what is left and its own</b>
/// (<see cref="RetryLadder.Lesser"/>, which the repair uses too). So a hung follow-up is killed by the
/// LAUNCHER, inside the launch budget — a timed-out launch the record can resume — and the turn's own
/// <c>ConsultationDeadline</c> (<c>T + T/4</c>) stays the backstop it was written as, never the thing that
/// stops an ordinary hang.</para>
/// <para><b><c>landed</c> is called after every launch</b>, before anything else can throw: the caller's
/// failure path reads the handle and the usage off whatever launches exist, and a snapshot that faults
/// after a launch must not make that launch invisible to it.</para>
/// </remarks>
public static class ConsultantTurn
{
    public static async Task<ConsultantTurnResult> RunAsync(
        ReviewerExecutor executor,
        IConsultantRuntime runtime,
        ReviewerInvocation first,
        Func<CancellationToken, Task<IReadOnlyList<TreeChange>>> changesSoFar,
        Action<ReviewerLaunch> landed,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var budget = first.Request.Timeout;
        var launched = await executor.LaunchOnceAsync(first, ct);
        landed(launched);

        var next = Offered(runtime, first, launched, RetryLadder.Remaining(clock.Elapsed, budget));
        if (next is null)
        {
            return Of(runtime, [launched], []);
        }

        var changes = await changesSoFar(ct);
        var left = RetryLadder.Remaining(clock.Elapsed, budget);
        if (changes.Count > 0 || left <= TimeSpan.Zero)
        {
            return Of(runtime, [launched], changes);
        }

        var second = await executor.LaunchOnceAsync(RetryLadder.Lesser(left, next), ct);
        landed(second);

        return Of(runtime, [launched, second], []);
    }

    /// <summary>
    /// The process ran, exited zero and said nothing — the one ending a follow-up exists for.
    /// </summary>
    /// <remarks>
    /// A terminal launch — not started, timed out, rate limited, a non-zero exit — is not "answered
    /// nothing": it is a failure with a reason of its own, and continuing a conversation into a quota is
    /// a second bill for the same refusal.
    /// </remarks>
    public static bool AnsweredNothing(ReviewerLaunch launched) =>
        launched.Terminal is null && string.IsNullOrWhiteSpace(launched.Answer);

    /// <summary>
    /// The conversation id the launches leave behind — the LATEST that names one, else empty.
    /// </summary>
    /// <remarks>
    /// Over a list because the caller's failure path holds exactly what <c>landed</c> gave it, which may be
    /// one launch or two — and one rule for both is how the result and the failure path cannot disagree
    /// about which conversation survived.
    /// </remarks>
    public static string SurvivingHandle(IConsultantRuntime runtime, IReadOnlyList<ReviewerLaunch> launches) =>
        launches
            .Reverse()
            .Select(launch => launch.Process is { } process ? runtime.ReadHandle(process) : string.Empty)
            .FirstOrDefault(handle => handle.Length > 0) ?? string.Empty;

    /// <summary>What the launches consumed between them, billed once — <see cref="Usage.None"/> for none.</summary>
    public static Usage UsageOf(IConsultantRuntime runtime, IReadOnlyList<ReviewerLaunch> launches) => launches.Count switch
    {
        0 => Usage.None,
        1 => launches[0].Usage,
        _ => ConsultationUsage.OfTwoLaunches(runtime.UsageIsCumulative, launches[0].Usage, launches[^1].Usage),
    };

    /// <summary>The follow-up the adapter offers, when the first launch answered nothing and time is left.</summary>
    private static ReviewerInvocation? Offered(IConsultantRuntime runtime, ReviewerInvocation first, ReviewerLaunch launched, TimeSpan left) =>
        AnsweredNothing(launched) && left > TimeSpan.Zero ? runtime.FollowUp(first, launched) : null;

    private static ConsultantTurnResult Of(IConsultantRuntime runtime, IReadOnlyList<ReviewerLaunch> launches, IReadOnlyList<TreeChange> changes) =>
        new(launches, changes, SurvivingHandle(runtime, launches), UsageOf(runtime, launches));
}
