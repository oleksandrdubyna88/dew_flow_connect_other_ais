using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Tests;

/// <summary>
/// The stand-ins a scenario that drives a REAL service or check over a Kimi row needs on its async flow (E5.5 and E5.6 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): the measured host, a fake person (<see cref="KimiFixture"/>), the lock root beside it and the played shim's
/// name — the same four things <c>AKimiConsultationScenarioTests</c> sets up for a consultation.
/// </summary>
internal static class KimiScenarioScopes
{
    /// <summary>Several scopes, given back in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose() => Array.ForEach([.. scopes.Reverse()], scope => scope.Dispose());
    }

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — call it SYNCHRONOUSLY so the values reach the test's own flow.</summary>
    public static IDisposable StandIn(KimiFixture person) => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));
}
