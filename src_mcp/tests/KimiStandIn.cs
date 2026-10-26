using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Tests;

/// <summary>
/// The person, the host and the lock root a Kimi adapter built BY NAME sees, for the check and the question-row scenarios of E5.5 and E5.6
/// (<c>AKimiConsultationScenarioTests.StandIn</c> is the same, private to that class). Set by a SYNCHRONOUS call, so the values reach the test's
/// own flow and every service call it awaits.
/// </summary>
internal static class KimiStandIn
{
    /// <summary>The fake person's places, the Windows host, the lock root beside the person's folder, and <see cref="PlayedKimi.Shim"/> as the shim.</summary>
    public static IDisposable Of(KimiFixture person)
    {
        // QWEN-FILL-BEGIN s56-standin test
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

        return new Scopes(
            WindowsStandIn.Begin(),
            KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
            KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, [])))
        );
        // QWEN-FILL-END s56-standin
    }
}
