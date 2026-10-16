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
/// <para><b>The person's own Kimi is looked for first</b> (decision 74): see <see cref="ForeignKimiWatch"/>. <b>It gates.</b> Busy past the time given is the shim's exit 69, a hold that cannot be taken at all its 65 — there is no
/// warning-only fallback, because the alternative to a refused turn is two refreshers on a rotating token.</para>
/// </remarks>
public static class KimiShimHold
{
    /// <summary>
    /// The hold for <paramref name="places"/>' owned root, waiting up to <paramref name="within"/> for another tree to let go — AFTER looking, read-only,
    /// for the person's own Kimi (<see cref="ForeignKimiWatch"/>, decision 74): the sign-in's locks serialise coai's launches only, so a <c>kimi</c> the
    /// person runs beside would race the rotating refresh token. It is waited for within the same deadline (the hold gets what is left), and past the
    /// deadline the turn is busy, naming the process.
    /// </summary>
    public static AcpHolding Taken(KimiPlaces places, TimeSpan within, ForeignKimiWatch watch) => Taken(places, within, watch, KimiCredentialsLock.HoldTree);

    /// <summary>The same, with the tree hold's take behind a seam — a test hands the NON-inheriting take (no test holds an inheritable handle in the test host).</summary>
    internal static AcpHolding Taken(KimiPlaces places, TimeSpan within, ForeignKimiWatch watch, Func<string, string, TimeSpan, TreeHolding> hold) =>
        watch.Wait(within) switch
        {
            ForeignKimiAnswer.Running running => new AcpHolding.Busy(ForeignKimiWatch.Sentence(running.Still[0])),
            ForeignKimiAnswer.Unreadable unreadable => new AcpHolding.Busy(ForeignKimiWatch.UnreadableSentence(unreadable.Reason)),
            ForeignKimiAnswer.Clear clear => TakenAfterTheLook(places, within - clear.Waited, hold),
            _ => new AcpHolding.Unusable("the foreign-Kimi wait answered something this shim does not know — refused"),
        };

    private static AcpHolding TakenAfterTheLook(KimiPlaces places, TimeSpan within, Func<string, string, TimeSpan, TreeHolding> hold)
    {
        var target = Junctions.Target(places.Link);
        if (target.Length == 0)
        {
            return new AcpHolding.Unusable($"the owned Kimi home {places.OwnedRoot} has no credentials junction at {places.Link}, so there is no sign-in to hold — refused (D19)");
        }

        return WithNoSurvivors(hold(target, places.OwnedRoot, within), target);
    }

    /// <summary>
    /// The A2-A3 seam (<see cref="IKimiSurvivors"/>), asked once BOTH tree files are held — the last point that can see a marker the previous shim's teardown wrote: the gate is non-inheritable and dies with the server, so another
    /// server's launch can pass it before that teardown has written anything. A standing marker gives the hold back and makes the turn Busy with the marker's sentence. Run synchronously: nothing may yield between the take and the agent's start.
    /// </summary>
    private static AcpHolding WithNoSurvivors(TreeHolding holding, string target)
    {
        if (holding is not TreeHolding.Taken taken)
        {
            return Held(holding, target);
        }

        var standing = KimiSurvivors.Gate.BlockedAsync(KimiCredentialsLock.MarkerPathFor(target), CancellationToken.None).GetAwaiter().GetResult();
        if (standing.Length == 0)
        {
            return Held(holding, target);
        }

        taken.Hold.Dispose();

        return new AcpHolding.Busy(standing);
    }

    /// <summary>What the lock answered, in the shim's words: held, busy (another tree is alive), or refused because two refreshers could race.</summary>
    internal static AcpHolding Held(TreeHolding holding, string target) => holding switch
    {
        TreeHolding.Taken taken => new AcpHolding.Held(taken.Hold),
        TreeHolding.Busy => new AcpHolding.Busy(
            $"another Kimi started on this sign-in is still running ({KimiCredentialsLock.TreePathFor(target)} is held) — it ends when its agent does; the turn is given up"),
        TreeHolding.Unusable unusable => new AcpHolding.Unusable($"the Kimi sign-in's tree hold cannot be taken ({unusable.Cause.Message}) — refused, because without it two refreshers could race (D19)"),
        _ => new AcpHolding.Unusable("the Kimi sign-in's tree hold cannot be taken — refused (D19)"),
    };
}
