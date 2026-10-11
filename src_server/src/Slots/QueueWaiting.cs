namespace CoaiServer;

/// <summary>
/// Why a queued job cannot start now — the sentence its status carries in <c>waiting</c>.
/// </summary>
/// <remarks>
/// <para>Story 5.3b of todo/PLAN_every_round_is_counted.md, owed by 5.3a's code round: since 5.3a a slot is parked until
/// the instant its vendor stated, and a job queued behind it heard nothing about when it could run. The sentence is
/// <see cref="SlotSelector.Explain"/>'s — written for exactly this and called by no status path until now — so the
/// catalog, the runner and a polling client read one account state in one vocabulary.</para>
/// <para>Said only when it is TRUE that no account can take the job: <see cref="SlotSelector.Explain"/> is the answer to
/// "why did <see cref="SlotSelector.Pick"/> find nothing", and a job that merely waits its turn behind others is told so by
/// its position. Read from the same registry the runner claims from, at the moment of the poll.</para>
/// </remarks>
public sealed class QueueWaiting(VendorCatalogHost catalog, SlotRegistry slots)
{
    /// <summary>The sentence for <paramref name="job"/> at <paramref name="nowUtc"/>, or empty when it waits for nothing.</summary>
    public string For(JobRecord job, DateTimeOffset nowUtc) =>
        job.Status == JobStatus.Queued && catalog.Current.Find(job.Vendor) is { } vendor
            ? WhyNone(slots.SlotsOf(vendor), nowUtc)
            : string.Empty;

    /// <summary><see cref="SlotSelector.Explain"/> when no account can take a job now; empty when one can.</summary>
    private static string WhyNone(IReadOnlyList<AccountSlot> accounts, DateTimeOffset nowUtc) =>
        SlotSelector.Pick(accounts, nowUtc) is null ? SlotSelector.Explain(accounts, nowUtc) : string.Empty;
}
