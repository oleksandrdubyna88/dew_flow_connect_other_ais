using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The product's side of the A2-A3 seam (<see cref="IKimiSurvivors"/>; §0 of both cornerstone specs of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): A3's reconcile answers "may this launch go on", and A3's
/// marker scan answers "does a marker keep this home". One class, so the three callers (<c>KimiLaunch.TakeAsync</c>, <c>KimiShimHold.Taken</c>, <c>KimiHome.Alive</c>) and the retirement sweep read the SAME rule.
/// </summary>
/// <remarks>
/// <para><b>Blocking is the reconcile's verdict, never the file's presence.</b> A marker whose every member is confirmed gone is cleared (the file deleted) and the launch goes on; a marker that cannot be
/// reconciled keeps the sign-in, with <see cref="KimiSurvivorGate.Sentence"/> — the manual way out first. A marker file that cannot be read is Blocked by the gate itself, so nothing here treats "cannot tell" as free.</para>
/// <para><b>A home is kept by what a marker NAMES</b> — and by a marker that cannot be read (<see cref="KimiSurvivorMarker.StandingFor"/> is fail-closed): an unreadable file may name any home. Asking never reconciles and
/// never signals — a home disposal is not the place to end a process.</para>
/// <para>The reconcile is awaited by the server's gate and BLOCKED on by the shim (<c>KimiShimHold</c>), which must not yield between the two tree files being held and the agent's start.</para>
/// </remarks>
public sealed class ReconcilingSurvivors(IProcessList listing, Func<string> lockRoot) : IKimiSurvivors
{
    /// <summary>This host's process listing and lock root.</summary>
    public static ReconcilingSurvivors Real { get; } = new(HostProcessList.Current, () => KimiCredentialsLock.Root);

    public async Task<string> BlockedAsync(string markerPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await KimiSurvivorGate.ReconcileAsync(markerPath, listing) is SurvivorVerdict.Blocked blocked ? KimiSurvivorGate.Sentence(blocked) : string.Empty;
    }

    public string KeepsHome(string ownedRoot) => KimiSurvivorMarker.StandingFor(lockRoot(), ownedRoot) switch
    {
        StandingMarkers.Found { Markers.Count: > 0 } found => Kept(ownedRoot, found.Markers[0]),
        StandingMarkers.Found => string.Empty,
        StandingMarkers.Unverifiable unverifiable => $"coai cannot tell whether a survivor marker names {ownedRoot} ({unverifiable.Reason}) — the home is kept until the marker is cleared or removed",
        _ => $"coai cannot tell whether a survivor marker names {ownedRoot} — the home is kept",
    };

    private static string Kept(string ownedRoot, StandingMarker standing) =>
        $"a Kimi started on {ownedRoot} may still be running (the survivor marker {standing.Path} names pid {Pids(standing.Marker)}) — the home is kept until the marker is cleared";

    private static string Pids(KimiSurvivorMarkerDto marker) =>
        marker.Marker.Tree.Count == 0 ? "none: it was written before the agent started" : string.Join(", ", marker.Marker.Tree.Select(member => member.Pid));
}
