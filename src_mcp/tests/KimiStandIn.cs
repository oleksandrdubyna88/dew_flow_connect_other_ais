using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Tests;

/// <summary>
/// The person, the host and the lock root a flow's Kimi adapters see — what every test that plays the Kimi shim over a <see cref="KimiFixture"/>
/// sets first (<see cref="AKimiConsultantCheckTests"/>, <see cref="AKimiQuestionRowTests"/>, <see cref="AKimiConsultationScenarioTests"/>).
/// </summary>
internal static class KimiStandIn
{
    /// <summary>Several scopes, given back in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var scope in scopes.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>
    /// Set by a SYNCHRONOUS call, so the values reach the test's own flow and every call it awaits. <paramref name="also"/> are the caller's own scopes
    /// (a scenario's calling identity), taken after these and given back before them.
    /// </summary>
    public static IDisposable Begin(KimiFixture person, params IDisposable[] also) => new Scopes(
        [
            WindowsStandIn.Begin(),
            KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
            KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))),
            .. also,
        ]);
}
