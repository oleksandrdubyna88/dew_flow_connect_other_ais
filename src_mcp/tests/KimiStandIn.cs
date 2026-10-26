using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Tests;

/// <summary>
/// The person, the host and the lock root a Kimi adapter built BY NAME sees, for the check and the question-row scenarios of E5.5 and E5.6
/// (and the E5.4 scenarios, with the scope of the caller they add). Set by a SYNCHRONOUS call, so the values reach the test's
/// own flow and every service call it awaits.
/// </summary>
internal static class KimiStandIn
{
    /// <summary>The fake person's places, the Windows host, the lock root beside the person's folder, and <see cref="PlayedKimi.Shim"/> as the shim, then the scopes the test adds.</summary>
    public static IDisposable Of(KimiFixture person, params IDisposable[] more) => new Scopes(
        [
            WindowsStandIn.Begin(),
            KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
            KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))),
            .. more,
        ]);

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
}
