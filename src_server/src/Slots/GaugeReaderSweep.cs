namespace CoaiServer;

/// <summary>
/// The startup sweep for gauge readers a dead server left behind — through the same slot-locked path every acquisition
/// takes, never a sweep of its own (story 6.1a of todo/PLAN_every_round_is_counted.md, the epic-6 risk consultation).
/// </summary>
/// <remarks>
/// <para>At boot, every configured slot is acquired for an instant and released: <c>SlotRegistry.AcquireAsync</c>
/// reconciles the previous holder's marker under the lock — kills what it names, confirms the exit, or blocks the
/// slot — and that is the whole sweep. A slot another process holds (a <c>login</c> in progress) is skipped; its holder
/// reconciled it on the way in.</para>
/// <para>A hosted service rather than a line in <c>Program.cs</c>, so it runs before the pump starts claiming and is
/// logged by the host's own logger; an exception here is logged and never stops the host — the next acquisition
/// reconciles again.</para>
/// </remarks>
public sealed class GaugeReaderSweep(VendorCatalogHost catalog, SlotRegistry slots, ILogger<GaugeReaderSweep> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var vendor in catalog.Current.Vendors)
        {
            foreach (var slot in slots.SlotsOf(vendor))
            {
                await SweepAsync(slot, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SweepAsync(AccountSlot slot, CancellationToken ct)
    {
        try
        {
            switch (await slots.AcquireAsync(slot, TimeSpan.Zero, ct: ct))
            {
                case Acquisition.Taken taken:
                    taken.Lease.Dispose();
                    break;
                case Acquisition.Blocked blocked:
                    log.LogWarning("{Vendor}/{Slot} stays unavailable: {Code}", slot.Vendor, slot.Name, blocked.Code);
                    break;
                default:
                    log.LogInformation("{Vendor}/{Slot} is held by another process; its holder reconciled it", slot.Vendor, slot.Name);
                    break;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "the reader sweep could not reconcile {Vendor}/{Slot}; the next acquisition will", slot.Vendor, slot.Name);
        }
    }
}
