using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The shim's side of the tree hold (E4.2 decision 10, E4.3 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the two inheritable files of a
/// running Kimi tree taken for the turn, in the shim's words. Its own unit so the profile stays a table of what Kimi is and the lock stays
/// a lock.
/// </summary>
/// <remarks>
/// <para><b>The sign-in is named by the owned junction, never by a profile.</b> The shim's environment is the redirected one — inside it
/// <c>USERPROFILE</c> IS the owned <c>os/</c> — so the person's <c>credentials</c> folder is read from the junction's own reparse data
/// (<see cref="Junctions.Target"/>); the lock folder is <see cref="KimiCredentialsLock.Root"/>, which the caller has pointed at <c>--locks</c>
/// for the turn (<see cref="KimiCredentialsLock.RootFor"/>) and which is never derived here.</para>
/// <para><b>It gates.</b> Busy past the time given is the shim's exit 69, a hold that cannot be taken at all its 65 — there is no
/// warning-only fallback, because the alternative to a refused turn is two refreshers on a rotating token.</para>
/// </remarks>
public static class KimiShimHold
{
    /// <summary>The hold for <paramref name="places"/>' owned root, waiting up to <paramref name="within"/> for another tree to let go.</summary>
    public static AcpHolding Taken(KimiPlaces places, TimeSpan within)
    {
        var target = Junctions.Target(places.Link);
        if (target.Length == 0)
        {
            return new AcpHolding.Unusable($"the owned Kimi home {places.OwnedRoot} has no credentials junction at {places.Link}, so there is no sign-in to hold — refused (D19)");
        }

        return Held(KimiCredentialsLock.HoldTree(target, places.OwnedRoot, within), target);
    }

    private static AcpHolding Held(TreeHolding holding, string target) => holding switch
    {
        TreeHolding.Taken taken => new AcpHolding.Held(taken.Hold),
        TreeHolding.Busy => new AcpHolding.Busy(
            $"another Kimi started on this sign-in is still running ({KimiCredentialsLock.TreePathFor(target)} is held) — it ends when its agent does; the turn is given up"),
        TreeHolding.Unusable unusable => new AcpHolding.Unusable($"the Kimi sign-in's tree hold cannot be taken ({unusable.Cause.Message}) — refused, because without it two refreshers could race (D19)"),
        _ => new AcpHolding.Unusable("the Kimi sign-in's tree hold cannot be taken — refused (D19)"),
    };
}
